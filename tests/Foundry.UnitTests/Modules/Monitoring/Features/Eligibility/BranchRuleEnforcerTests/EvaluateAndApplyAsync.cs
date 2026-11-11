using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.BranchRuleEnforcerTests;

public sealed class EvaluateAndApplyAsync
{
    private const string RulesToken = "rules_token";
    private const string SettingsUrl = "https://github.com/owner/repo/settings/rules";

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly RepositoryEligibility BranchProtectionViolations = new RepositoryEligibility.Ineligible(
    [
        EligibilityViolation.AllowDirectPushes(),
        EligibilityViolation.AllowForcePushes(),
    ]);

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            pollInterval: null).ValueOrThrow();

    private static GitHubCredential CreateAccount(string? branchRulesToken = RulesToken) =>
        GitHubCredential.Create(
            "acct",
            "ghp_account",
            BaseUrl.Create("https://github.com").ValueOrThrow(),
            branchRulesToken);

    private static BranchRuleEnforcer CreateSut(ScriptedEvaluator evaluator, RecordingProviderFactory factory) =>
        new(evaluator, factory, NullLogger<BranchRuleEnforcer>.Instance);

    [Fact]
    public async Task WhenOnlyBranchProtectionViolationsAndWriteTakesEffect_AppliesWithRulesTokenThenReReads()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations, reReadEligibility: new RepositoryEligibility.Eligible());
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);
        MonitoredRepository repository = CreateRepository();

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            repository, CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(BranchRulesOutcome.AppliedKind));
        factory.TokensUsed.ShouldBe([RulesToken]);
        factory.Provider.ApplyCalls.ShouldBe(1);
        evaluator.FullEvaluations.ShouldBe(1);
        evaluator.BranchRuleEvaluations.ShouldBe(1);
        repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task WhenWriteSucceedsButReReadStillIneligible_FailsAsNotEffectiveAndKeepsReReadEligibility()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations, reReadEligibility: BranchProtectionViolations);
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);
        MonitoredRepository repository = CreateRepository();

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            repository, CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(
            BranchRulesOutcome.FailedKind,
            BranchRulesOutcome.ProviderErrorReason,
            SettingsUrl,
            BranchRuleEnforcer.NotEffectiveDetail));
        repository.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
    }

    [Fact]
    public async Task WhenAlreadyEligible_DoesNotWriteAndReportsAlreadyPresent()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(new RepositoryEligibility.Eligible());
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(BranchRulesOutcome.AlreadyPresentKind, SettingsUrl: SettingsUrl));
        factory.TokensUsed.ShouldBeEmpty();
        evaluator.BranchRuleEvaluations.ShouldBe(0);
    }

    public static TheoryData<string> AccessViolationRules => new()
    {
        EligibilityViolationInfo.CannotPushRule("owner/repo"),
        EligibilityViolationInfo.NoCredentialRule("owner"),
        EligibilityViolationInfo.CredentialUnreadableRule,
    };

    [Theory]
    [MemberData(nameof(AccessViolationRules))]
    public async Task WhenIneligibleWithAccessViolation_DoesNotWriteAndSkipsWithAccessFailure(string rule)
    {
        // Arrange
        ScriptedEvaluator evaluator = new(new RepositoryEligibility.Ineligible(
            [new EligibilityViolation(rule), EligibilityViolation.AllowDeletion()]));
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(
            BranchRulesOutcome.SkippedKind,
            BranchRulesOutcome.AccessFailureReason,
            SettingsUrl));
        factory.TokensUsed.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UnreachableReason.NeverProbed)]
    [InlineData(UnreachableReason.RateLimited)]
    [InlineData(UnreachableReason.BranchRulesUnavailable)]
    public async Task WhenUnreachable_DoesNotWriteAndSkipsAsUnverifiable(UnreachableReason reason)
    {
        // Arrange
        ScriptedEvaluator evaluator = new(new RepositoryEligibility.Unreachable(reason));
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(
            BranchRulesOutcome.SkippedKind,
            BranchRulesOutcome.UnverifiableReason,
            SettingsUrl));
        factory.TokensUsed.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoRulesToken_DoesNotWriteAndSkipsWithNoRulesToken()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations);
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), CreateAccount(branchRulesToken: null), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(
            BranchRulesOutcome.SkippedKind,
            BranchRulesOutcome.NoRulesTokenReason,
            SettingsUrl));
        factory.TokensUsed.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRulesTokenIsUnreadable_DoesNotWriteAndSkipsWithNoRulesToken()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations);
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);
        GitHubCredential account = CreateAccount();
        Credential.ApplyUnreadableBranchRulesToken(account);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), account, applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.Reason.ShouldBe(BranchRulesOutcome.NoRulesTokenReason);
        factory.TokensUsed.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNotRequested_DoesNotWriteAndReportsNotRequested()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations);
        RecordingProviderFactory factory = new(Result.Ok());
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), CreateAccount(), applyBranchRules: false, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(BranchRulesOutcome.NotRequestedKind, SettingsUrl: SettingsUrl));
        factory.TokensUsed.ShouldBeEmpty();
        evaluator.FullEvaluations.ShouldBe(1);
    }

    [Fact]
    public async Task WhenWriteIsForbidden_FailsWithInsufficientPermissionAndDoesNotReRead()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations);
        RecordingProviderFactory factory = new(
            Result.Fail(BranchRuleWriteErrors.InsufficientPermission("needs Administration")));
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);
        MonitoredRepository repository = CreateRepository();

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            repository, CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(
            BranchRulesOutcome.FailedKind,
            BranchRulesOutcome.InsufficientPermissionReason,
            SettingsUrl,
            "needs Administration"));
        evaluator.BranchRuleEvaluations.ShouldBe(0);
        repository.Eligibility.ShouldBe(BranchProtectionViolations);
    }

    [Fact]
    public async Task WhenWriteFailsOtherwise_FailsWithProviderError()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations);
        RecordingProviderFactory factory = new(Result.Fail(new Error("GitHub.ProviderError", "plan rejects rulesets")));
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldBe(new BranchRulesOutcome(
            BranchRulesOutcome.FailedKind,
            BranchRulesOutcome.ProviderErrorReason,
            SettingsUrl,
            "plan rejects rulesets"));
    }

    [Fact]
    public async Task WhenWriteThrows_FailsWithProviderError()
    {
        // Arrange
        ScriptedEvaluator evaluator = new(BranchProtectionViolations);
        RecordingProviderFactory factory = new(new HttpRequestException("boom"));
        BranchRuleEnforcer sut = CreateSut(evaluator, factory);

        // Act
        BranchRulesOutcome outcome = await sut.EvaluateAndApplyAsync(
            CreateRepository(), CreateAccount(), applyBranchRules: true, Now, CancellationToken.None);

        // Assert
        outcome.ShouldSatisfyAllConditions(
            o => o.Kind.ShouldBe(BranchRulesOutcome.FailedKind),
            o => o.Reason.ShouldBe(BranchRulesOutcome.ProviderErrorReason));
        evaluator.BranchRuleEvaluations.ShouldBe(0);
    }

    private sealed class ScriptedEvaluator(
        RepositoryEligibility fullEligibility,
        RepositoryEligibility? reReadEligibility = null) : IRepositoryEligibilityEvaluator
    {
        public int FullEvaluations { get; private set; }

        public int BranchRuleEvaluations { get; private set; }

        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            FullEvaluations++;
            repo.SetEligibility(fullEligibility);
            return Task.CompletedTask;
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            BranchRuleEvaluations++;
            repo.SetEligibility(reReadEligibility
                ?? throw new InvalidOperationException("No re-read was scripted."));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProviderFactory : IIssueProviderFactory
    {
        public RecordingProviderFactory(Result applyResult)
        {
            Provider = new ApplyOnlyProvider(applyResult, null);
        }

        public RecordingProviderFactory(Exception applyException)
        {
            Provider = new ApplyOnlyProvider(null, applyException);
        }

        public ApplyOnlyProvider Provider { get; }

        public List<string> TokensUsed { get; } = [];

        public IIssueProvider CreateProvider(Credential credential, string token)
        {
            TokensUsed.Add(token);
            return Provider;
        }
    }

    private sealed class ApplyOnlyProvider(Result? applyResult, Exception? applyException) : IIssueProvider
    {
        public int ApplyCalls { get; private set; }

        public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            if (applyException is not null)
            {
                throw applyException;
            }

            return Task.FromResult(applyResult!);
        }

        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug, string pullRequestUrl, DateTimeOffset since, PostedCommentIds postedComments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> DeleteBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<Checks>> GetChecksAsync(
            RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
