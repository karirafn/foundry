using System.Net;
using System.Net.Http.Json;

using Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

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

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Verifies the step-3 guard: when a token resolves to a login that is DIFFERENT from the
/// holder of the claimed namespaces, DuplicateAccount.Find returns null (same-login guard
/// does not fire) and the fully-claimed-by-others guard fires instead, returning 409 with an
/// UpdateAccountConflictResponse body (reason: ClaimedElsewhere), leaving the rotated
/// account's namespaces intact. The server composes the message naming the namespace and holder.
///
/// Token routing:
///   ghp_first_token   → first-user  (create first account; derives namespace "first-user")
///   ghp_second_token  → second-user (create second account; derives namespace "second-user")
///   ghp_third_claimed → third-user  (rotate second account; derives "first-user" — claimed
///                                    by a DIFFERENT login, so DuplicateAccount.Find is null
///                                    and the step-3 guard fires → 409)
///   ghp_third_free    → third-user  (rotate second account; derives "third-user" — unclaimed
///                                    → 200)
/// </summary>
public sealed class WhenTokenClaimedElsewhereByOtherLogin : IAsyncDisposable
{
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";

    // Token used to rotate second account to a namespace already held by first-user account.
    // The resolved login is "third-user", which differs from the holder "first-user",
    // so DuplicateAccount.Find returns null and the step-3 fully-claimed guard fires.
    private const string ThirdTokenClaimed = "ghp_third_claimed";

    // Token used to rotate second account to a namespace held by nobody.
    private const string ThirdTokenFree = "ghp_third_free";

    private const string FirstAccountName = "first-user";
    private const string SecondAccountName = "second-user";
    private const string ThirdAccountName = "third-user";

    private static readonly Dictionary<string, string> TokenToListing = new()
    {
        [FirstToken] = """[{"full_name":"first-user/repo","private":false,"permissions":{"push":true}}]""",
        [SecondToken] = """[{"full_name":"second-user/repo","private":false,"permissions":{"push":true}}]""",

        // ThirdTokenClaimed derives "first-user" namespace — already claimed by the first account.
        [ThirdTokenClaimed] = """[{"full_name":"first-user/repo","private":false,"permissions":{"push":true}}]""",

        // ThirdTokenFree derives "third-user" namespace — claimed by nobody.
        [ThirdTokenFree] = """[{"full_name":"third-user/repo","private":false,"permissions":{"push":true}}]""",
    };

    private static readonly Dictionary<string, string> TokenToName = new()
    {
        [FirstToken] = FirstAccountName,
        [SecondToken] = SecondAccountName,
        [ThirdTokenClaimed] = ThirdAccountName,
        [ThirdTokenFree] = ThirdAccountName,
    };

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenClaimedElsewhereByOtherLogin()
    {
        TokenKeyedStub stub = new(TokenToName);

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(_ => stub);

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new TokenKeyedListingFakeHandler(TokenToListing)),
                    NullLogger<GitHubHttpClient>.Instance,
                    new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System));
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
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialCreationResult? firstResult = await firstResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        firstResult.ShouldNotBeNull();

        HttpResponseMessage secondResponse = await _client.PostAsJsonAsync(
            new Uri("/api/accounts", UriKind.Relative),
            secondBody,
            TestContext.Current.CancellationToken);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        CredentialCreationResult? secondResult = await secondResponse.Content
            .ReadFromJsonAsync<CredentialCreationResult>(TestContext.Current.CancellationToken);
        secondResult.ShouldNotBeNull();

        return (firstResult.Credential.Id, secondResult.Credential.Id);
    }

    [Fact]
    public async Task WhenAllDerivedNamespacesClaimedByDifferentLogin_Returns409WithStructuredBody()
    {
        // Arrange — seed first-user (claims "first-user") and second-user (claims "second-user").
        // Rotate second-user with ThirdTokenClaimed: resolves to "third-user", derives "first-user".
        // "first-user" is claimed by the first account whose login is "first-user" — DIFFERENT from
        // "third-user" — so DuplicateAccount.Find returns null and the step-3 guard fires.
        (Guid firstId, Guid secondId) = await SeedTwoAccountsAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = ThirdTokenClaimed,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{secondId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — 409 with UpdateAccountConflictResponse (reason: ClaimedElsewhere)
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        UpdateAccountConflictResponse? body = await response.Content
            .ReadFromJsonAsync<UpdateAccountConflictResponse>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Reason.ShouldBe(UpdateAccountConflictReason.ClaimedElsewhere);
        // AC7: server composes the message naming namespace and holder — client no longer assembles it
        body.Message.ShouldContain($"{FirstAccountName} (held by {FirstAccountName})");

        // Assert — second account's namespace is unchanged (not stranded on zero)
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(secondId),
                TestContext.Current.CancellationToken);
        credential.ShouldNotBeNull();
        credential.Namespaces.ShouldHaveSingleItem();
        credential.Namespaces.ShouldContain(ns => ns.Value == SecondAccountName);
    }

    [Fact]
    public async Task WhenDerivedNamespaceIsUnclaimed_Returns200AndUpdatesNamespaces()
    {
        // Arrange — seed first-user (claims "first-user") and second-user (claims "second-user").
        // Rotate second-user with ThirdTokenFree: resolves to "third-user", derives "third-user"
        // which is claimed by nobody — the step-3 guard does not fire.
        (_, Guid secondId) = await SeedTwoAccountsAsync();

        object updateBody = new
        {
            baseUrl = "https://github.com",
            token = ThirdTokenFree,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{secondId}", UriKind.Relative),
            updateBody,
            TestContext.Current.CancellationToken);

        // Assert — rotation succeeds and namespaces reflect the new token's derived owner
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        CredentialUpdateResult? result = await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        result.Credential.Namespaces.ShouldBe([ThirdAccountName], ignoreOrder: true);
    }

    // Routes each ValidateToken call to a fixed account name based on the token value.
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
}
