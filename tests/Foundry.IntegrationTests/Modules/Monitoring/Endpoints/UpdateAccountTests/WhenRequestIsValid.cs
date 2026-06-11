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

public sealed class WhenRequestIsValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestIsValid()
    {
        ValidateToken.Response validResponse = new(IsValid: true, IsAuthFailure: false, MissingScopes: []);
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOkWithUpdatedAccount()
    {
        // Arrange
        object createBody = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_original_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        AccountSummary? created = await createResponse.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        object updateBody = new
        {
            name = "Updated GitHub",
            baseUrl = "https://github.com",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AccountSummary? account = await response.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        account.ShouldNotBeNull();
        account.ShouldSatisfyAllConditions(
            () => account.Id.ShouldBe(created.Id),
            () => account.Name.ShouldBe("Updated GitHub"),
            () => account.ProviderType.ShouldBe("github"),
            () => account.HasToken.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenTokenProvided_UpdatesToken()
    {
        // Arrange
        object createBody = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_original_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        AccountSummary? created = await createResponse.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        object updateBody = new
        {
            name = "My GitHub",
            baseUrl = "https://github.com",
            token = "ghp_new_token",
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AccountSummary? account = await response.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        account.ShouldNotBeNull();
        account.HasToken.ShouldBeTrue();
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
