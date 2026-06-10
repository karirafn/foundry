using System.Net;

using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.ScanOAuthCredentialsTests;

public sealed class WhenScanFails : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenScanFails()
    {
        _factory = new FoundryWebAppFactory(services =>
        {
            services.RemoveAll<IOAuthCredentialScanner>();
            services.AddScoped<IOAuthCredentialScanner>(_ => new StubOAuthCredentialScanner(
                Result<OAuthCredentials>.Fail(new Error(
                    "Settings.OAuthCredentialsNotFound",
                    "No OAuth credentials file was found."))));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsBadRequestWithErrorMessage()
    {
        // Arrange — stub configured to return an error in constructor

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings/oauth/scan", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string content = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        content.ShouldNotBeNullOrEmpty();
    }

    private sealed class StubOAuthCredentialScanner(Result<OAuthCredentials> result) : IOAuthCredentialScanner
    {
        public Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
