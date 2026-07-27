using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Verifies that when a GitHub token's write-access probe returns 403 (Forbidden),
/// the create-account endpoint rejects the request with 400 and names the missing write access.
/// </summary>
public sealed class WhenTokenLacksWriteAccess : IAsyncDisposable
{
    private const string ResolvedAccountName = "octocat";
    private const string Token = "ghp_missing_contents_token";

    // One writable repo so namespace derivation succeeds and the probe has a target.
    private const string OctocatListingJson = """
        [
          { "full_name": "octocat/hello-world", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenLacksWriteAccess()
    {
        ValidateToken.Response validResponse = new(
            IsValid: true,
            IsAuthFailure: false,
            ScopesVerified: true,
            MissingScopes: [],
            AccountName: ResolvedAccountName);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));

            // Listing GETs return repos; probe POSTs return 403 (Contents permission missing).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ContentsProbeBlockedFakeHandler(OctocatListingJson))));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsBadRequestNamingTheMissingPermission()
    {
        // Arrange
        object body = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = Token,
        };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        responseBody.ShouldContain("Contents");
    }

    /// <summary>
    /// Handler that returns the listing JSON for GET requests, but returns 403 for all probe
    /// POSTs (simulating a token that lacks the Contents write permission).
    /// </summary>
    private sealed class ContentsProbeBlockedFakeHandler(string listingJson) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (StaticListingFakeHandler.IsProbePost(request))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(listingJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
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
