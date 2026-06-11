using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.DeleteAccountTests;

public sealed class WhenAccountExists : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountExists()
    {
        ValidateToken.Response validResponse = new(IsValid: true, IsAuthFailure: false, MissingScopes: []);
        _factory = new FoundryWebAppFactory(services =>
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
    public async Task ReturnsNoContent()
    {
        // Arrange
        object createBody = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        AccountSummary? created = await createResponse.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        // Act
        HttpResponseMessage response = await _client.DeleteAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AccountNoLongerAppearsInGetAccounts()
    {
        // Arrange
        object createBody = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        AccountSummary? created = await createResponse.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();

        await _client.DeleteAsync(
            new Uri($"/api/accounts/{created.Id}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage getResponse = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<AccountSummary>? accounts = await getResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<AccountSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        accounts.ShouldNotContain(a => a.Id == created.Id);
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
