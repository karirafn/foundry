using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

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
    public async Task ReturnsCreatedWithAccountSummary()
    {
        // Arrange
        object body = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        AccountSummary? account = await response.Content
            .ReadFromJsonAsync<AccountSummary>(TestContext.Current.CancellationToken);
        account.ShouldNotBeNull();
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("My GitHub"),
            () => account.ProviderType.ShouldBe("github"),
            () => account.BaseUrl.ShouldBe("https://github.com/"),
            () => account.HasToken.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenProviderTypeIsMixedCase_ReturnsCreated()
    {
        // Arrange
        object body = new
        {
            name = "My GitHub Mixed",
            providerType = "GitHub",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AppearsInSubsequentGetAccounts()
    {
        // Arrange
        object body = new
        {
            name = "My GitHub",
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
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
        accounts.ShouldContain(a => a.Name == "My GitHub");
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
