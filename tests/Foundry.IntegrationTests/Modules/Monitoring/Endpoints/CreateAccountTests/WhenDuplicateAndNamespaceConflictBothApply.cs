using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.CreateAccountTests;

/// <summary>
/// Verifies AC6: when a create request is a duplicate-account (same login, same-login sibling
/// covers the entire derived set), the response reason is DuplicateAccount with an empty
/// Conflicts list — no takeover panel is offered. Under never-steal semantics the same-login
/// sibling's namespace is excluded from the conflict set, so the request is a duplicate only.
/// </summary>
public sealed class WhenDuplicateAndNamespaceConflictBothApply : IAsyncDisposable
{
    // Both tokens resolve to the same account name "octocat" (same login).
    // Both tokens also derive the same "octocat" namespace — the second request is a
    // duplicate account (same login, sibling covers the entire derived set).
    // The handler's duplicate guard fires → DuplicateAccount is returned.
    private const string ResolvedAccountName = "octocat";
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";

    private static readonly Dictionary<string, string> TokenToListing = new()
    {
        [FirstToken] = """[{"full_name":"octocat/repo-a","private":false,"permissions":{"push":true}}]""",
        [SecondToken] = """[{"full_name":"octocat/repo-b","private":false,"permissions":{"push":true}}]""",
    };

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenDuplicateAndNamespaceConflictBothApply()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new TokenKeyedValidateTokenStub(new Dictionary<string, string>
                {
                    [FirstToken] = ResolvedAccountName,
                    [SecondToken] = ResolvedAccountName,
                }));

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

    [Fact]
    public async Task WhenRequestIsBothDuplicateAndNamespaceConflict_Returns409WithDuplicateAccountReasonAndEmptyConflicts()
    {
        // Arrange — create the first account; it claims "octocat" under account name "octocat".
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

        // Act — submit a second token that resolves to the SAME login ("octocat") and derives
        // the SAME namespace ("octocat"). Under never-steal semantics the same-login sibling's
        // namespace is excluded from the conflict set, making this a duplicate only.
        // The duplicate guard fires → DuplicateAccount is returned.
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

        // Assert — reason is DuplicateAccount (duplicate wins), Conflicts is empty (no takeover offered)
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        CreateAccountConflictResponse? body = await response.Content
            .ReadFromJsonAsync<CreateAccountConflictResponse>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Reason.ShouldBe(CreateAccountConflictReason.DuplicateAccount);
        body.Conflicts.ShouldBeEmpty();
        body.Message.ShouldContain(ResolvedAccountName);
    }
}
