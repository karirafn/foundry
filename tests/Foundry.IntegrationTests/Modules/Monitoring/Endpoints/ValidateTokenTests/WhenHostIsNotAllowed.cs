using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

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
    private const int BadRequestStatus = 400;

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
    public async Task ReturnsHostNotAllowedAsProblemDetails()
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
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Status.ShouldBe(BadRequestStatus);
        problem.Type.ShouldEndWith("ProviderHost.NotAllowed");
        string detail = problem.Detail.ShouldNotBeNull();
        detail.ShouldNotBeEmpty();
        detail.ShouldContain("attacker.example.com");
    }
}
