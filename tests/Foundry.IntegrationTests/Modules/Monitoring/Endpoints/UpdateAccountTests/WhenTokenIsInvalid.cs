using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

public sealed class WhenTokenIsInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenIsInvalid()
    {
        // The stub returns invalid so both create and update see it as invalid,
        // but update is the operation under test.
        ValidateToken.Response invalidResponse = new(IsValid: false, IsAuthFailure: true, ScopesVerified: false, MissingScopes: [], AccountName: null);
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(invalidResponse)));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenNewTokenIsInvalid_ReturnsBadRequest()
    {
        // Arrange — insert directly via DbContext since token validation blocks creation through POST
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = "ghp_invalid_token",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenTokenResolvesNullIdentity_ReturnsBadRequest()
    {
        // Arrange — token is valid but provider returned no identity (AccountName = null)
        ValidateToken.Response unresolvedResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            ScopesVerified: true,
            MissingScopes: [],
            AccountName: null);

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(unresolvedResponse)));
        });
        using HttpClient client = factory.CreateClient();

        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(factory);

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenTokenResolvesOversizedName_ReturnsBadRequest()
    {
        // Arrange — provider returns a name longer than 200 characters (max allowed length)
        string oversizedName = new('a', 201);
        ValidateToken.Response oversizedResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            ScopesVerified: true,
            MissingScopes: [],
            AccountName: oversizedName);

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(oversizedResponse)));
        });
        using HttpClient client = factory.CreateClient();

        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(factory);

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenTokenResolvesNameWithControlCharacter_ReturnsBadRequest()
    {
        // Arrange — provider returns a name containing a control character (newline)
        ValidateToken.Response controlCharResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            ScopesVerified: true,
            MissingScopes: [],
            AccountName: "valid-prefix\ninjected");

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(controlCharResponse)));
        });
        using HttpClient client = factory.CreateClient();

        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(factory);

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed class StubValidateTokenHandler(Result<ValidateToken.Response> result)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
