using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.ScanOAuthCredentialsTests;

public sealed class WhenScanSucceeds : IAsyncDisposable
{
    private static readonly DateTimeOffset CredentialsExpiry = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static readonly OAuthCredentials StubCredentials = new(
        "access-token-value",
        "refresh-token-value",
        CredentialsExpiry,
        "pro");

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenScanSucceeds()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IOAuthCredentialScanner>();
            services.AddScoped<IOAuthCredentialScanner>(_ => new StubOAuthCredentialScanner(
                Result<OAuthCredentials>.Ok(StubCredentials)));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOkWithPresenceMetadata()
    {
        // Arrange — stub already configured in constructor

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings/oauth/scan", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ScanOAuthCredentials.OAuthScanResponse? scanResponse = await response.Content
            .ReadFromJsonAsync<ScanOAuthCredentials.OAuthScanResponse>(TestContext.Current.CancellationToken);
        scanResponse.ShouldNotBeNull();
        scanResponse.ShouldSatisfyAllConditions(
            () => scanResponse.AccessTokenPresent.ShouldBeTrue(),
            () => scanResponse.RefreshTokenPresent.ShouldBeTrue(),
            () => scanResponse.SubscriptionType.ShouldBe("pro"),
            () => scanResponse.ExpiresAt.ShouldBe(CredentialsExpiry));
    }

    [Fact]
    public async Task DoesNotExposeRawTokensInResponse()
    {
        // Arrange — stub already configured in constructor

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings/oauth/scan", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        responseBody.ShouldNotContain("access-token-value");
        responseBody.ShouldNotContain("refresh-token-value");
    }

    private sealed class StubOAuthCredentialScanner(Result<OAuthCredentials> result) : IOAuthCredentialScanner
    {
        public Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
