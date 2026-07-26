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
/// Verifies that adding a second credential claiming a namespace already owned by an existing
/// credential on the same host results in a 409 Conflict.
/// </summary>
public sealed class WhenNamespaceAlreadyClaimed : IAsyncDisposable
{
    // Both tokens resolve to the same owner namespace "octocat" (different names).
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";

    private const string GitHubListingJson = """
        [
          { "full_name": "octocat/repo", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenNamespaceAlreadyClaimed()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new TokenKeyedStub(new Dictionary<string, string>
                {
                    [FirstToken] = "first-user",
                    [SecondToken] = "second-user",
                }));

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(new HttpClient(new ListingFakeHandler(HttpStatusCode.OK, GitHubListingJson))));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenSecondCredentialClaimsAlreadyClaimedNamespace_ReturnsConflict()
    {
        // Arrange — create first credential that claims "octocat" namespace
        object firstBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = FirstToken,
        };

        HttpResponseMessage firstResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            firstBody,
            TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Act — try to add a second credential that would also claim "octocat"
        object secondBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = SecondToken,
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            secondBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private sealed class TokenKeyedStub(Dictionary<string, string> tokenToName)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            string accountName = tokenToName.TryGetValue(query.Token, out string? name) ? name : "unknown";
            ValidateToken.Response response = new(
                IsValid: true,
                IsAuthFailure: false,
                ScopesVerified: true,
                MissingScopes: [],
                AccountName: accountName);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class ListingFakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
