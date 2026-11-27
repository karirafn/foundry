using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
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

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.UpdateAccountTests;

/// <summary>
/// Saving a new account token through PUT /api/accounts/{id} applies the branch rules to the
/// account's opted-in repositories with that token, and the response's affected repositories report
/// the post-enforcement eligibility.
/// </summary>
public sealed class WhenAccountTokenIsRotated : IAsyncDisposable
{
    private const string AccountToken = "github_pat_account_token";
    private const string RotatedToken = "github_pat_rotated_token";

    private const string ListingJson = """
        [
          { "full_name": "owner/opted-in", "private": false, "permissions": { "push": true } },
          { "full_name": "owner/opted-out", "private": false, "permissions": { "push": true } }
        ]
        """;

    private readonly ProviderState _state = new();
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenAccountTokenIsRotated()
    {
        Dictionary<string, string> tokenToListing = new()
        {
            [AccountToken] = ListingJson,
            [RotatedToken] = ListingJson,
        };

        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueProviderFactory>();
            services.AddSingleton(_state);
            services.AddScoped<IIssueProviderFactory, StatefulProviderFactory>();

            services.RemoveAll<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>();
            services.AddScoped<IQueryHandler<ValidateToken.Query, ValidateToken.Response>>(
                _ => new StubValidateTokenHandler());

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new TokenKeyedListingFakeHandler(tokenToListing)),
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

    private async Task<Guid> SeedAccountAsync()
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "Rules Org",
            token: AccountToken);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        return accountId;
    }

    private async Task<CreateRepositoryResponse> AddRepositoryAsync(Guid accountId, string slug, bool applyBranchRules)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            new { slug, applyBranchRules },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CreateRepositoryResponse? created = await response.Content
            .ReadFromJsonAsync<CreateRepositoryResponse>(TestContext.Current.CancellationToken);
        return created.ShouldNotBeNull();
    }

    private async Task<CredentialUpdateResult> PutAsync(Guid accountId, object body)
    {
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri($"/api/accounts/{accountId}", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        CredentialUpdateResult? result = await response.Content
            .ReadFromJsonAsync<CredentialUpdateResult>(TestContext.Current.CancellationToken);
        return result.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenTokenRotated_AppliesRulesToOptedInReposOnlyWithTheNewTokenAndReportsThemEligible()
    {
        // Arrange — the original token may not write branch rules, so both repos land ineligible.
        _state.ApplyResult = Result.Fail(BranchRuleWriteErrors.InsufficientPermission("needs Administration"));
        Guid accountId = await SeedAccountAsync();
        CreateRepositoryResponse optedIn = await AddRepositoryAsync(accountId, "owner/opted-in", applyBranchRules: true);
        await AddRepositoryAsync(accountId, "owner/opted-out", applyBranchRules: false);
        optedIn.BranchRules.Reason.ShouldBe(BranchRulesOutcome.InsufficientPermissionReason);
        _state.ApplyResult = Result.Ok();
        _state.Applied.Clear();

        // Act
        CredentialUpdateResult result = await PutAsync(
            accountId,
            new { baseUrl = "https://github.com", token = RotatedToken });

        // Assert
        _state.Applied.ShouldBe([("owner/opted-in", RotatedToken)]);
        result.AffectedRepositories.ShouldBe(
            [new AffectedRepository(optedIn.Repository.Id, "owner/opted-in", "ineligible", "eligible")]);
    }

    [Fact]
    public async Task WhenOnlyTheBaseUrlIsSaved_RunsNoEnforcement()
    {
        // Arrange
        _state.ApplyResult = Result.Fail(BranchRuleWriteErrors.InsufficientPermission("needs Administration"));
        Guid accountId = await SeedAccountAsync();
        await AddRepositoryAsync(accountId, "owner/opted-in", applyBranchRules: true);
        _state.Applied.Clear();

        // Act
        CredentialUpdateResult result = await PutAsync(
            accountId,
            new { baseUrl = "https://github.com", token = (string?)null });

        // Assert
        _state.Applied.ShouldBeEmpty();
        result.AffectedRepositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenWriteIsStillForbidden_UpdateSucceedsAndEligibilityIsUnchanged()
    {
        // Arrange
        _state.ApplyResult = Result.Fail(BranchRuleWriteErrors.InsufficientPermission("needs Administration"));
        Guid accountId = await SeedAccountAsync();
        await AddRepositoryAsync(accountId, "owner/opted-in", applyBranchRules: true);
        _state.Applied.Clear();

        // Act
        CredentialUpdateResult result = await PutAsync(
            accountId,
            new { baseUrl = "https://github.com", token = RotatedToken });

        // Assert
        result.Credential.TokenStatus.ShouldBe("present");
        _state.Applied.ShouldBe([("owner/opted-in", RotatedToken)]);
        result.AffectedRepositories.ShouldBeEmpty();
    }

    private sealed class ProviderState
    {
        public ConcurrentDictionary<string, bool> RulesInPlace { get; } = new();

        public Result ApplyResult { get; set; } = Result.Ok();

        public ConcurrentQueue<(string Slug, string Token)> Applied { get; } = new();
    }

    private sealed class StatefulProviderFactory(ProviderState state) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            new StatefulProvider(state, token);
    }

    private sealed class StatefulProvider(ProviderState state, string token) : IIssueProvider
    {
        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<BranchProtection>.Ok(new BranchProtection(
                "main",
                RejectDirectPushes: state.RulesInPlace.ContainsKey(slug.FullPath),
                RejectForcePushes: state.RulesInPlace.ContainsKey(slug.FullPath),
                RejectDeletion: state.RulesInPlace.ContainsKey(slug.FullPath))));

        public Task<Result> ApplyBranchRulesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            state.Applied.Enqueue((slug.FullPath, token));
            if (state.ApplyResult.IsSuccess)
            {
                state.RulesInPlace[slug.FullPath] = true;
            }

            return Task.FromResult(state.ApplyResult);
        }

        public Task<Result<IssueListing>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IssueListing>.Ok(new IssueListing([], IsComplete: true)));

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([], OmittedCommentCount: 0, NewestCommentAt: null)));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<bool>> DeleteBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CommitSha? publishedHead,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<Checks>> GetChecksAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<Checks>.Ok(new Checks.NoneDetected()));
    }
}
