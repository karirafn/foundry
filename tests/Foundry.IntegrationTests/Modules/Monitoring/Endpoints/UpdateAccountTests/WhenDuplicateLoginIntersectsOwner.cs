using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies duplicate-account detection when two accounts share the same provider login
/// but the incoming token intersects an existing account's namespace claims.
/// AC#2: a rotation whose derived owner set intersects a different credential's claims
/// with the same resolved login is rejected with 409 only when the retained set (derived
/// minus sibling-held) is empty — i.e. all derived namespaces are already held by a sibling.
/// AC#1: a rotation that retains at least one non-sibling namespace (same login, partial or
/// no overlap) succeeds; sibling-held namespaces are subtracted, not stolen.
/// </summary>
public sealed class WhenDuplicateLoginIntersectsOwner : IAsyncDisposable
{
    // Token-keyed routing: each token maps to a fixed account name and namespace listing.
    // ghp_first_token         → first-user  (create first account, namespace "first-user")
    // ghp_second_token        → second-user (create second account, namespace "second-user")
    // ghp_colliding_token     → first-user  (update second account; same login + same namespace → 409
    //                                         because retained set after subtracting sibling claim is empty)
    // ghp_legitimate_token    → first-user  (update second account; same login but different namespace → 200)
    // ghp_partial_overlap     → first-user  (update second account; same login, derives "first-user" AND
    //                                         "first-user-org" → 200 because "first-user-org" is retained
    //                                         after subtracting A's "first-user" claim)
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";
    private const string CollidingToken = "ghp_colliding_token";
    private const string LegitimateToken = "ghp_legitimate_token";
    private const string PartialOverlapToken = "ghp_partial_overlap";
    private const string FirstAccountName = "first-user";
    private const string SecondAccountName = "second-user";

    // Note: CollidingToken and FirstToken both derive namespace "first-user" — that is the
    // point of the collision test. LegitimateToken derives "first-user-org" which does not
    // intersect with account A's claim of "first-user". PartialOverlapToken derives both
    // "first-user" (held by A) and "first-user-org" (held by no one), so after subtracting
    // A's claim the retained set is ["first-user-org"], which is non-empty → 200.
    private static readonly Dictionary<string, string> TokenToListing = new()
    {
        [FirstToken] = """[{"full_name":"first-user/repo","private":false,"permissions":{"push":true}}]""",
        [SecondToken] = """[{"full_name":"second-user/repo","private":false,"permissions":{"push":true}}]""",
        [CollidingToken] = """[{"full_name":"first-user/repo","private":false,"permissions":{"push":true}}]""",
        [LegitimateToken] = """[{"full_name":"first-user-org/repo","private":false,"permissions":{"push":true}}]""",
        [PartialOverlapToken] = """[{"full_name":"first-user/repo","private":false,"permissions":{"push":true}},{"full_name":"first-user-org/repo","private":false,"permissions":{"push":true}}]""",
    };

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly TokenKeyedStub _stub;

    public WhenDuplicateLoginIntersectsOwner()
    {
        _stub = new TokenKeyedStub(new Dictionary<string, string>
        {
            [FirstToken] = FirstAccountName,
            [SecondToken] = SecondAccountName,
            [CollidingToken] = FirstAccountName,
            [LegitimateToken] = FirstAccountName,
            [PartialOverlapToken] = FirstAccountName,
        });
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(_ => _stub);

            // Probe-aware: each token returns its specific listing; probe POSTs return 422 (Granted).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new TokenKeyedListingFakeHandler(TokenToListing)),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions())))));
        });
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(Guid FirstId, Guid SecondId)> SeedTwoAccountsAsync()
    {
        object firstBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = FirstToken,
        };

        object secondBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = SecondToken,
        };

        HttpResponseMessage firstResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            firstBody,
            TestContext.Current.CancellationToken);
        CredentialCreationResult? firstResult = await firstResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        firstResult.ShouldNotBeNull();

        HttpResponseMessage secondResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            secondBody,
            TestContext.Current.CancellationToken);
        CredentialCreationResult? secondResult = await secondResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        secondResult.ShouldNotBeNull();

        return (firstResult.Credential.Id, secondResult.Credential.Id);
    }

    [Fact]
    public async Task ReturnsConflict()
    {
        // Arrange — create account A (first-user/first-user) and account B (second-user/second-user).
        // Update B with CollidingToken: resolves to "first-user" and derives namespace "first-user",
        // which intersects A's existing claim. This must be rejected with 409.
        (_, Guid secondId) = await SeedTwoAccountsAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = CollidingToken,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{secondId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — same login + intersecting owner namespace → rejected as DuplicateAccount
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        UpdateAccountConflictResponse? body = await response.Content
            .ReadFromJsonAsync<UpdateAccountConflictResponse>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Reason.ShouldBe(UpdateAccountConflictReason.DuplicateAccount);
        body.Message.ShouldContain(FirstAccountName);
    }

    [Fact]
    public async Task WhenSameLoginDifferentNamespace_ReturnsOk()
    {
        // Arrange — create account A (first-user/first-user) and account B (second-user/second-user).
        // Update B with LegitimateToken: resolves to "first-user" (same login as A) but derives
        // namespace "first-user-org" which does NOT intersect A's "first-user" claim.
        // This is the normal two-account case: same provider login, different owner namespace (AC#1).
        (_, Guid secondId) = await SeedTwoAccountsAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = LegitimateToken,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{secondId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — same login but non-intersecting namespace → allowed
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhenTokenDerivesPartialOverlapWithSibling_ReturnsOkAndSubtractsSiblingNamespace()
    {
        // Arrange — create account A (first-user / "first-user") and account B (second-user / "second-user").
        // Update B with PartialOverlapToken: resolves to "first-user" (same login as A) and derives
        // namespaces "first-user" (held by A) and "first-user-org" (held by no one).
        // The handler must subtract A's claim and accept the rotation; B's retained namespace is "first-user-org".
        (_, Guid secondId) = await SeedTwoAccountsAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = PartialOverlapToken,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{secondId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — partial overlap with sibling → accepted; sibling namespace subtracted, not stolen
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        CredentialUpdateResult result = (await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        result.Credential.Namespaces.ShouldBe(["first-user-org"], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenNoTokenSupplied_NameUnchangedDoesNotConflict()
    {
        // Arrange — updating without a token keeps the existing name; no conflict expected
        ValidateToken.Response validResponse = new(
            Kind: ValidateToken.Kinds.Authenticated,
            AccountName: "octocat",
            MissingScopes: [],
            DetectedProvider: null);

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler(Result<ValidateToken.Response>.Ok(validResponse)));

            // Probe-aware: listing GETs return repos; probe POSTs return 422 (Granted).
            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new StaticListingFakeHandler(
                        System.Net.HttpStatusCode.OK,
                        """[{"full_name":"octocat/repo","private":false,"permissions":{"push":true}}]""")),
                    NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions())))));
        });
        using HttpClient client = factory.CreateClient();

        object createBody = new
        {
            providerType = "github",
            baseUrl = "https://github.com",
            token = "ghp_test_token",
        };

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            createBody,
            TestContext.Current.CancellationToken);

        CredentialCreationResult? createdResult = await createResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        createdResult.ShouldNotBeNull();

        object updateBody = new { baseUrl = "https://github.com" };

        // Act
        HttpResponseMessage response = await client.PutAsJsonAsync(
            new Uri($"/api/accounts/{createdResult.Credential.Id}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Routes each ValidateToken call to a fixed account name based on the token value.
    // This is robust against extra internal calls — the result depends solely on the token.
    private sealed class TokenKeyedStub(Dictionary<string, string> tokenToName)
        : IQueryHandler<ValidateToken.Query, ValidateToken.Response>
    {
        public Task<Result<ValidateToken.Response>> HandleAsync(
            ValidateToken.Query query,
            CancellationToken cancellationToken)
        {
            string accountName = tokenToName.TryGetValue(query.Token, out string? name)
                ? name
                : "default-user";
            ValidateToken.Response response = new(
                Kind: ValidateToken.Kinds.Authenticated,
                AccountName: accountName,
                MissingScopes: [],
                DetectedProvider: null);
            return Task.FromResult(Result<ValidateToken.Response>.Ok(response));
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
