using System.Net;
using System.Net.Http.Json;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

/// <summary>
/// Proves that POST /api/accounts/validate-token returns 400 before making any provider calls
/// when the base URL host is not in the implicit allowlist (github.com, gitlab.com) and the
/// operator allowlist in GlobalSettings is empty. No GlobalSettings row needs to be seeded
/// because GetAllowedProviderHostsAsync returns [] when the row is absent.
/// </summary>
public sealed class WhenHostIsNotAllowed : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenHostIsNotAllowed()
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
    public async Task ReturnsBadRequest()
    {
        // Arrange
        object body = new
        {
            providerType = "github",
            baseUrl = "https://attacker.example.com",
            token = "ghp_victim_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReturnsHostNotAllowedMessage()
    {
        // Arrange
        object body = new
        {
            providerType = "github",
            baseUrl = "https://attacker.example.com",
            token = "ghp_victim_token",
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldContain("attacker.example.com");
    }
}
