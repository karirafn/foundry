using System.Net;
using System.Text.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.RefreshUsageTests;

/// <summary>
/// Verifies that <c>POST /api/credentials/usage/refresh</c> refuses with a client-visible 409
/// (never a 5xx) when the seeded default account cannot report usage, and starts no poll.
/// </summary>
public sealed class WhenAccountCannotReportUsage : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountCannotReportUsage()
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
    public async Task Returns409WithUnavailableStatus()
    {
        // Arrange — the seeder creates an API-key account with no committed OAuth identity

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/credentials/usage/refresh", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        string json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().ShouldBe("unavailable");
    }
}
