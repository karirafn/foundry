using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
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
/// Verifies that submitting a second token whose resolved login matches an existing
/// account's name, and whose derived namespace intersects that account's claims,
/// returns a 409 with a CreateAccountConflictResponse body where reason is DuplicateAccount
/// and Conflicts is empty (duplicate-wins precedence: no takeover offered).
/// </summary>
public sealed class WhenTokenDuplicatesExistingAccount : IAsyncDisposable
{
    // Both tokens resolve to the same account name (same login).
    private const string ResolvedAccountName = "octocat";
    private const string FirstToken = "ghp_first_token";
    private const string SecondToken = "ghp_second_token";

    // Both tokens reach the same namespace ("octocat"), so the second is a duplicate.
    private static readonly Dictionary<string, string> TokenToListing = new()
    {
        [FirstToken] = """[{"full_name":"octocat/repo-a","private":false,"permissions":{"push":true}}]""",
        [SecondToken] = """[{"full_name":"octocat/repo-b","private":false,"permissions":{"push":true}}]""",
    };

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenTokenDuplicatesExistingAccount()
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

            // Both tokens return listings under the same "octocat" namespace.
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
    public async Task WhenSecondTokenDuplicatesExistingAccountLogin_Returns409WithDuplicateAccountReason()
    {
        // Arrange — create the first account; it claims "octocat".
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

        // Act — submit a second token that resolves to the same login and namespace.
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

        // Assert — 409 with reason DuplicateAccount, empty Conflicts (duplicate wins; no takeover offered)
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
