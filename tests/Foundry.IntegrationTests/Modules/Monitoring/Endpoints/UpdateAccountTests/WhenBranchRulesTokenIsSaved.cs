using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
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
/// Saving a branch-rules token through PUT /api/accounts/{id} applies the branch rules to the
/// account's opted-in repositories, and the response's affected repositories report the
/// post-enforcement eligibility.
/// </summary>
public sealed class WhenBranchRulesTokenIsSaved : IAsyncDisposable
{
    private const string AccountToken = "ghp_account_token";
    private const string RulesToken = "ghp_rules_token";

    private readonly ProviderState _state = new();
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenBranchRulesTokenIsSaved()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueProviderFactory>();
            services.AddSingleton(_state);
            services.AddScoped<IIssueProviderFactory, StatefulProviderFactory>();

            services.RemoveAll<GitHubHttpClient>();
            services.AddSingleton(
                new GitHubHttpClient(
                    new HttpClient(new ProbeGrantedFakeHandler()),
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

    private async Task<Guid> SeedAccountWithoutRulesTokenAsync()
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "Rules Org",
            token: AccountToken,
            branchRulesToken: null);
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
    public async Task WhenTokenSaved_AppliesRulesToOptedInReposOnlyAndReportsThemEligible()
    {
        // Arrange — both repos land ineligible: the account has no rules token yet.
        Guid accountId = await SeedAccountWithoutRulesTokenAsync();
        CreateRepositoryResponse optedIn = await AddRepositoryAsync(accountId, "owner/opted-in", applyBranchRules: true);
        await AddRepositoryAsync(accountId, "owner/opted-out", applyBranchRules: false);
        optedIn.BranchRules.Reason.ShouldBe(BranchRulesOutcome.NoRulesTokenReason);

        // Act
        CredentialUpdateResult result = await PutAsync(
            accountId,
            new { baseUrl = "https://github.com", token = (string?)null, branchRulesToken = RulesToken });

        // Assert
        _state.Applied.ShouldBe([("owner/opted-in", RulesToken)]);
        result.AffectedRepositories.ShouldBe(
            [new AffectedRepository(optedIn.Repository.Id, "owner/opted-in", "ineligible", "eligible")]);
    }

    [Fact]
    public async Task WhenTokenCleared_RunsNoEnforcement()
    {
        // Arrange
        Guid accountId = await SeedAccountWithoutRulesTokenAsync();
        await AddRepositoryAsync(accountId, "owner/opted-in", applyBranchRules: true);

        // Act
        CredentialUpdateResult result = await PutAsync(
            accountId,
            new { baseUrl = "https://github.com", token = (string?)null, clearBranchRulesToken = true });

        // Assert
        _state.Applied.ShouldBeEmpty();
        result.AffectedRepositories.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenWriteIsForbidden_UpdateSucceedsAndEligibilityIsUnchanged()
    {
        // Arrange
        _state.ApplyResult = Result.Fail(BranchRuleWriteErrors.InsufficientPermission("needs Administration"));
        Guid accountId = await SeedAccountWithoutRulesTokenAsync();
        await AddRepositoryAsync(accountId, "owner/opted-in", applyBranchRules: true);

        // Act
        CredentialUpdateResult result = await PutAsync(
            accountId,
            new { baseUrl = "https://github.com", token = (string?)null, branchRulesToken = RulesToken });

        // Assert
        result.Credential.BranchRulesTokenStatus.ShouldBe("present");
        _state.Applied.Count.ShouldBe(1);
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

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(false, false)));

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
