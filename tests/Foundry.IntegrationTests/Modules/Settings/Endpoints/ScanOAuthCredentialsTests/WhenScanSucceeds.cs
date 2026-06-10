using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Domain;
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
        _factory = new FoundryWebAppFactory(services =>
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
    public async Task ReturnsOkWithCredentialData()
    {
        // Arrange — stub already configured in constructor

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings/oauth/scan", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        OAuthCredentials? credentials = await response.Content
            .ReadFromJsonAsync<OAuthCredentials>(TestContext.Current.CancellationToken);
        credentials.ShouldNotBeNull();
        credentials.ShouldSatisfyAllConditions(
            () => credentials.AccessToken.ShouldBe("access-token-value"),
            () => credentials.RefreshToken.ShouldBe("refresh-token-value"),
            () => credentials.SubscriptionType.ShouldBe("pro"),
            () => credentials.ExpiresAt.ShouldBe(CredentialsExpiry));
    }

    private sealed class StubOAuthCredentialScanner(Result<OAuthCredentials> result) : IOAuthCredentialScanner
    {
        public Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
