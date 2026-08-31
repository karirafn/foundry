using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Verifies the never-steal semantics on the create path when a same-login sibling already
/// claims some — but not all — of the incoming token's derived namespaces.
/// Criterion 1: account is created claiming only the unclaimed namespace.
/// Criterion 2: the response is not a namespace-conflict for the same-login sibling's namespace.
/// </summary>
public sealed class WhenSameLoginSiblingClaimsPartialNamespaceSet : IAsyncDisposable
{
    private const string SharedLogin = "octocat";
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";

    // First token derives only "octocat"; second token derives both "octocat" (sibling) and "octocat-org" (unclaimed).
    private static readonly Dictionary<string, string> TokenToListing = new()
    {
        [FirstToken] = """[{"full_name":"octocat/repo-a","private":false,"permissions":{"push":true}}]""",
        [SecondToken] = """
            [
              {"full_name":"octocat/repo-b","private":false,"permissions":{"push":true}},
              {"full_name":"octocat-org/repo-c","private":false,"permissions":{"push":true}}
            ]
            """,
    };

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSameLoginSiblingClaimsPartialNamespaceSet()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new TokenKeyedValidateTokenStub(new Dictionary<string, string>
                {
                    [FirstToken] = SharedLogin,
                    [SecondToken] = SharedLogin,
                }));

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new TokenKeyedListingFakeHandler(TokenToListing)),
                    NullLogger<GitHubHttpClient>.Instance,
                    new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
                    new InMemoryProviderRateBudget(),
                    TimeProvider.System));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenSecondTokenDerivesUnclaimedOwner_CreatesAccountClaimingOnlyUnclaimedOwner()
    {
        // Arrange — first account created; it claims "octocat".
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

        // Act — second token resolves to same login and derives both "octocat" (sibling) and "octocat-org" (unclaimed).
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

        // Assert — criterion 1: created, claiming only "octocat-org" (never-steal subtracts "octocat")
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        CredentialCreationResult? result = await response.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.Namespaces.ShouldContain("octocat-org");
        result.Credential.Namespaces.ShouldNotContain("octocat");
    }

    [Fact]
    public async Task WhenSecondTokenDerivesUnclaimedOwner_NoConflictBodyReturned()
    {
        // Arrange — first account created; it claims "octocat".
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

        // Act — second token derives both "octocat" (sibling) and "octocat-org" (unclaimed).
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

        // Assert — criterion 2: created (not a 409 conflict); the same-login sibling namespace is never offered for transfer
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
