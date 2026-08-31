using System.Net;
using System.Net.Http.Json;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Verifies that adding a second credential claiming a namespace already owned by an existing
/// credential on the same host results in a structured 409 Conflict listing the holder details.
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
                new GitHubHttpClient(new HttpClient(new ListingFakeHandler(HttpStatusCode.OK, GitHubListingJson)), NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenSecondCredentialClaimsAlreadyClaimedNamespace_Returns409WithStructuredBody()
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
        CredentialCreationResult? firstResult = await firstResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        firstResult.ShouldNotBeNull();

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

        // Assert — structured 409 with reason NamespaceConflict and holder details
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        CreateAccountConflictResponse? conflictBody = await response.Content
            .ReadFromJsonAsync<CreateAccountConflictResponse>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        conflictBody.ShouldNotBeNull();
        conflictBody.Reason.ShouldBe(CreateAccountConflictReason.NamespaceConflict);
        conflictBody.Message.ShouldNotBeNullOrEmpty();
        conflictBody.Conflicts.Count.ShouldBe(1);

        NamespaceConflict conflict = conflictBody.Conflicts[0];
        conflict.ShouldSatisfyAllConditions(
            () => conflict.Namespace.ShouldBe("octocat"),
            () => conflict.HolderCredentialId.ShouldBe(firstResult.Credential.Id),
            () => conflict.HolderName.ShouldBe("first-user"));
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
                Kind: ValidateToken.Kinds.Authenticated,
                AccountName: accountName,
                MissingScopes: [],
                DetectedProvider: null);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
        }
    }

    private sealed class ListingFakeHandler(HttpStatusCode statusCode, string responseBody) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (StaticListingFakeHandler.IsProbePost(request))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
            }

            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
