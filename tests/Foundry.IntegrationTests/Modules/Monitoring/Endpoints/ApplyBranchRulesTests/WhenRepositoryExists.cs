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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ApplyBranchRulesTests;

public sealed class WhenRepositoryExists : IAsyncDisposable
{
    private const string AccountToken = "ghp_account_token";
    private const string RulesToken = "ghp_rules_token";

    private readonly ProviderState _state = new();
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRepositoryExists()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueProviderFactory>();
            services.AddSingleton(_state);
            services.AddScoped<IIssueProviderFactory, StatefulProviderFactory>();

            // Probe-aware: probe POSTs return 422 (Granted) so the evaluator proceeds to the
            // branch-rules GET served by the stateful stub provider.
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

    private async Task<Guid> SeedAccountAsync(string? branchRulesToken = RulesToken)
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "Rules Org",
            token: AccountToken,
            branchRulesToken: branchRulesToken);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        return accountId;
    }

    private async Task<Guid> SeedRepositoryAsync(Guid accountId, bool applyBranchRules)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri($"/api/accounts/{accountId}/repositories", UriKind.Relative),
            new { slug = "owner/repo", applyBranchRules },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        CreateRepositoryResponse created = (await response.Content
            .ReadFromJsonAsync<CreateRepositoryResponse>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        return created.Repository.Id;
    }

    private async Task<(HttpStatusCode Status, ApplyBranchRulesResponse? Body)> ApplyAsync(
        Guid accountId,
        Guid repositoryId)
    {
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/accounts/{accountId}/repositories/{repositoryId}/apply-branch-rules", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        ApplyBranchRulesResponse? applied = response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<ApplyBranchRulesResponse>(TestContext.Current.CancellationToken)
            : null;

        return (response.StatusCode, applied);
    }

    private async Task<bool> ReadStoredIntentAsync(Guid repositoryId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        MonitoredRepository stored = await dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .SingleAsync(r => r.Id == MonitoredRepositoryId.From(repositoryId), TestContext.Current.CancellationToken);
        return stored.ApplyBranchRules;
    }

    [Fact]
    public async Task WhenOptedOutAtAddAndOnlyBranchRulesAreMissing_AppliesThemAndFlipsTheIntent()
    {
        // Arrange — added with applyBranchRules=false: ineligible, nothing written
        Guid accountId = await SeedAccountAsync();
        Guid repositoryId = await SeedRepositoryAsync(accountId, applyBranchRules: false);
        _state.ApplyTokens.ShouldBeEmpty();
        (await ReadStoredIntentAsync(repositoryId)).ShouldBeFalse();

        // Act
        (HttpStatusCode status, ApplyBranchRulesResponse? body) = await ApplyAsync(accountId, repositoryId);

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.BranchRules.ShouldBe(new BranchRulesOutcome(BranchRulesOutcome.AppliedKind));
        body.Repository.Id.ShouldBe(repositoryId);
        body.Repository.Eligibility.ShouldNotBeNull().Status.ShouldBe("eligible");
        _state.ApplyTokens.ShouldBe([RulesToken]);
        (await ReadStoredIntentAsync(repositoryId)).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenRulesAreAlreadyInPlace_ReturnsAlreadyPresentWithoutWriting()
    {
        // Arrange
        Guid accountId = await SeedAccountAsync();
        Guid repositoryId = await SeedRepositoryAsync(accountId, applyBranchRules: false);
        _state.RulesInPlace = true;

        // Act
        (HttpStatusCode status, ApplyBranchRulesResponse? body) = await ApplyAsync(accountId, repositoryId);

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.BranchRules.Kind.ShouldBe(BranchRulesOutcome.AlreadyPresentKind);
        body.Repository.Eligibility.ShouldNotBeNull().Status.ShouldBe("eligible");
        _state.ApplyTokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenWriteIsForbidden_ReturnsFailedInsufficientPermissionAndStaysIneligible()
    {
        // Arrange
        Guid accountId = await SeedAccountAsync();
        Guid repositoryId = await SeedRepositoryAsync(accountId, applyBranchRules: false);
        _state.ApplyResult = Result.Fail(BranchRuleWriteErrors.InsufficientPermission("needs Administration"));

        // Act
        (HttpStatusCode status, ApplyBranchRulesResponse? body) = await ApplyAsync(accountId, repositoryId);

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.BranchRules.ShouldSatisfyAllConditions(
            o => o.Kind.ShouldBe(BranchRulesOutcome.FailedKind),
            o => o.Reason.ShouldBe(BranchRulesOutcome.InsufficientPermissionReason),
            o => o.SettingsUrl.ShouldBe("https://github.com/owner/repo/settings/rules"));
        body.Repository.Eligibility.ShouldNotBeNull().Status.ShouldBe("ineligible");
        (await ReadStoredIntentAsync(repositoryId)).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAccountHasNoRulesToken_SkipsWithNoRulesToken()
    {
        // Arrange
        Guid accountId = await SeedAccountAsync(branchRulesToken: null);
        Guid repositoryId = await SeedRepositoryAsync(accountId, applyBranchRules: false);

        // Act
        (HttpStatusCode status, ApplyBranchRulesResponse? body) = await ApplyAsync(accountId, repositoryId);

        // Assert
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body.BranchRules.ShouldSatisfyAllConditions(
            o => o.Kind.ShouldBe(BranchRulesOutcome.SkippedKind),
            o => o.Reason.ShouldBe(BranchRulesOutcome.NoRulesTokenReason));
        _state.ApplyTokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAccountDoesNotServeTheRepository_ReturnsNotFoundWithoutWriting()
    {
        // Arrange — a second account on the same host that does not cover "owner"
        Guid accountId = await SeedAccountAsync();
        Guid repositoryId = await SeedRepositoryAsync(accountId, applyBranchRules: false);
        Guid otherAccountId = await AccountSeeder.SeedGitHubAccountAsync(
            _factory,
            name: "Other Org",
            token: "ghp_other",
            branchRulesToken: "ghp_other_rules");
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, otherAccountId, "other");

        // Act
        (HttpStatusCode status, _) = await ApplyAsync(otherAccountId, repositoryId);

        // Assert
        status.ShouldBe(HttpStatusCode.NotFound);
        _state.ApplyTokens.ShouldBeEmpty();
        (await ReadStoredIntentAsync(repositoryId)).ShouldBeFalse();
    }

    private sealed class ProviderState
    {
        public bool RulesInPlace { get; set; }

        public Result ApplyResult { get; set; } = Result.Ok();

        public ConcurrentQueue<string> ApplyTokens { get; } = new();
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
                RejectDirectPushes: state.RulesInPlace,
                RejectForcePushes: state.RulesInPlace,
                RejectDeletion: state.RulesInPlace)));

        public Task<Result> ApplyBranchRulesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
        {
            state.ApplyTokens.Enqueue(token);
            if (state.ApplyResult.IsSuccess)
            {
                state.RulesInPlace = true;
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
