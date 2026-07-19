using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.GetAccountsTests;

public sealed class WhenNoAccountsExist : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNoAccountsExist()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsEmptyList()
    {
        // Arrange — no accounts seeded

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/accounts", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<CredentialSummary>? accounts = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<CredentialSummary>>(TestContext.Current.CancellationToken);
        accounts.ShouldNotBeNull();
        accounts.ShouldBeEmpty();
    }
}
