using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

public sealed class ResolveAsync
{
    private const string DefaultBranchName = "feat/1-my-feature";
    private const string PrUrl = "https://github.com/owner/repo/pull/42";
    private const int DefaultCooldownMinutes = 60;

    private static ActiveRun CreateActiveRun(string branchName = DefaultBranchName)
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        return starting.Activate(
            ContainerId.From("container-123"),
            BranchName.From(branchName),
            MonitoredRepositoryId.New());
    }

    private static WorkerOutcomeResolver BuildResolver(
        IPostExitProviderQueries providerQueries,
        IContainerOutputParser? outputParser = null)
    {
        return new WorkerOutcomeResolver(
            providerQueries,
            outputParser ?? new NullContainerOutputParser(),
            prRetryDelay: TimeSpan.Zero,
            prRetryAttempts: 3);
    }

    // -------------------------------------------------------------------------
    // Step 5a — MR-state first
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenMrIsMergedWithUrl_ReturnsCompleted()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch mergedMr = new(MergeRequestPresence.Merged, PrUrl);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults: [Result<MergeRequestByBranch>.Ok(mergedMr)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Completed completed = outcome.ShouldBeOfType<WorkerOutcome.Completed>();
        completed.ShouldSatisfyAllConditions(
            () => completed.PullRequestUrl.Value.ShouldBe(PrUrl),
            () => completed.BranchName.Value.ShouldBe(DefaultBranchName));
    }

    [Fact]
    public async Task WhenMrIsMergedWithNullUrl_ReturnsIndeterminate()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch mergedMrNoUrl = new(MergeRequestPresence.Merged, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults: [Result<MergeRequestByBranch>.Ok(mergedMrNoUrl)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Indeterminate>();
    }

    [Fact]
    public async Task WhenMrIsOpen_ReturnsReview()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch openMr = new(MergeRequestPresence.Open, PrUrl);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults: [Result<MergeRequestByBranch>.Ok(openMr)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Review review = outcome.ShouldBeOfType<WorkerOutcome.Review>();
        review.PullRequestUrl.Value.ShouldBe(PrUrl);
    }

    [Fact]
    public async Task WhenMrIsClosedAndBranchHasCommits_ReturnsContinuableFailure()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch closedMr = new(MergeRequestPresence.Closed, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            mrResults: [Result<MergeRequestByBranch>.Ok(closedMr)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.BranchName.Value.ShouldBe(DefaultBranchName);
    }

    [Fact]
    public async Task WhenMrIsClosedAndBranchHasNoCommits_ReturnsFailure()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch closedMr = new(MergeRequestPresence.Closed, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults: [Result<MergeRequestByBranch>.Ok(closedMr)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    [Fact]
    public async Task WhenMrIsOpenWithNullUrl_ReturnsIndeterminateWithDistinctOpenMrCode()
    {
        // Arrange — Open MR with null WebUrl must use a distinct error code from Merged+null-URL
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch openMrNoUrl = new(MergeRequestPresence.Open, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults: [Result<MergeRequestByBranch>.Ok(openMrNoUrl)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Indeterminate indeterminate = outcome.ShouldBeOfType<WorkerOutcome.Indeterminate>();
        indeterminate.Error.Code.ShouldBe("MergeRequest.OpenMissingUrl");
    }

    [Fact]
    public async Task WhenMrQueryReturnsTransientError_ReturnsIndeterminate()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        Error transientError = new("Provider.Unavailable", "Service temporarily unavailable");
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults: [Result<MergeRequestByBranch>.Fail(transientError)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Indeterminate>();
    }

    [Fact]
    public async Task WhenMrQueryReturnsNotFoundError_ReturnsIndeterminate()
    {
        // Arrange — NotFound on the MR-LIST endpoint means repo-not-found (auth/repo error), not "no MR"
        ActiveRun run = CreateActiveRun();
        Error notFoundError = new Error("Provider.NotFound", "Not found") { Kind = ErrorKind.NotFound };
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults: [Result<MergeRequestByBranch>.Fail(notFoundError)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Indeterminate>();
    }

    // -------------------------------------------------------------------------
    // Step 5b — no MR fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenNoMrAndExitZeroWithCommitsAndMrAppearsOnRepoll_ReturnsReview()
    {
        // Arrange — first query returns None, repoll finds an Open MR
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        MergeRequestByBranch openMr = new(MergeRequestPresence.Open, PrUrl);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            mrResults:
            [
                Result<MergeRequestByBranch>.Ok(noneMr),
                Result<MergeRequestByBranch>.Ok(openMr),
            ]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Review review = outcome.ShouldBeOfType<WorkerOutcome.Review>();
        review.PullRequestUrl.Value.ShouldBe(PrUrl);
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithCommitsAndNoMrAfterRetries_ReturnsContinuableFailureWithBranchName()
    {
        // Arrange — all queries return None (no PR found after retries); branch is preserved as ContinuableFailure
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.ShouldSatisfyAllConditions(
            () => failure.FailureReason.ShouldBeOfType<FailureReason.ContainerError>(),
            () => failure.BranchName.Value.ShouldBe(DefaultBranchName));
    }

    [Fact]
    public async Task WhenRepollFindsMergedMr_ReturnsCompleted()
    {
        // Arrange — first query returns None, repoll discovers a Merged MR → Completed, not Review
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        MergeRequestByBranch mergedMr = new(MergeRequestPresence.Merged, PrUrl);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            mrResults:
            [
                Result<MergeRequestByBranch>.Ok(noneMr),
                Result<MergeRequestByBranch>.Ok(mergedMr),
            ]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Completed completed = outcome.ShouldBeOfType<WorkerOutcome.Completed>();
        completed.PullRequestUrl.Value.ShouldBe(PrUrl);
    }

    [Fact]
    public async Task WhenRepollFindsClosedMrAndBranchHasCommits_ReturnsContinuableFailure()
    {
        // Arrange — first query returns None; commits=true triggers repoll; repoll finds Closed MR;
        // second commits check (inside ResolveClosedAsync) also returns true → ContinuableFailure
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        MergeRequestByBranch closedMr = new(MergeRequestPresence.Closed, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            mrResults:
            [
                Result<MergeRequestByBranch>.Ok(noneMr),
                Result<MergeRequestByBranch>.Ok(closedMr),
            ]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.BranchName.Value.ShouldBe(DefaultBranchName);
    }

    [Fact]
    public async Task WhenRepollFindsClosedMrAndBranchHasNoCommits_ReturnsFailure()
    {
        // Arrange — first query returns None; commits=true triggers repoll; repoll finds Closed MR;
        // second commits check (inside ResolveClosedAsync) returns false → Failure
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        MergeRequestByBranch closedMr = new(MergeRequestPresence.Closed, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            mrResults:
            [
                Result<MergeRequestByBranch>.Ok(noneMr),
                Result<MergeRequestByBranch>.Ok(closedMr),
            ],
            commitsResults:
            [
                Result<bool>.Ok(true),   // first call: in ResolveNoMrAsync → triggers repoll
                Result<bool>.Ok(false),  // second call: in ResolveClosedAsync → no commits → Failure
            ]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommits_ReturnsUnchanged()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Unchanged>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndUsageLimited_ReturnsFailureWithUsageLimited()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(4);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new UsageLimitedParser(resetsAt);
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: "some output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.UsageLimited>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndUsageLimited_ContainerOutputIsPreserved()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(4);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new UsageLimitedParser(resetsAt);
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: "usage limit output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.ContainerOutput.ShouldBe("usage limit output");
    }

    [Fact]
    public async Task WhenNoMrAndNonZeroExitWithCommits_ReturnsContinuableFailure()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.BranchName.Value.ShouldBe(DefaultBranchName);
    }

    [Fact]
    public async Task WhenNoMrAndNonZeroExitWithNoCommits_ReturnsFailure()
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    [Fact]
    public async Task WhenNoMrAndNullExitWithNoCommits_ReturnsFailure()
    {
        // Arrange — null exit treated same as non-zero for failure classification
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: null,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    [Fact]
    public async Task WhenBranchCommitsQueryReturnsNotFound_TreatedAsNoCommits()
    {
        // Arrange — NotFound on commits query = definitively no commits; exit 0 → Unchanged
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        Error notFoundError = new Error("Provider.BranchNotFound", "Branch not found") { Kind = ErrorKind.NotFound };
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Fail(notFoundError),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Unchanged>();
    }

    [Fact]
    public async Task WhenBranchCommitsQueryReturnsTransientError_ReturnsIndeterminate()
    {
        // Arrange — transient commits error → Indeterminate
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        Error transientError = new("Provider.Unavailable", "Service temporarily unavailable");
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Fail(transientError),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Indeterminate>();
    }

    [Fact]
    public async Task WhenNoMrAndNonZeroExitWithNoCommitsAndNoResultLine_ReturnsFailureWithBootstrapFailed()
    {
        // Arrange — NoResultLine + non-zero exit → WorkerBootstrapFailed (heuristic)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new NoResultLineParser();
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.WorkerBootstrapFailed>();
    }

    [Fact]
    public async Task WhenNoMrAndNonZeroExitAndUsageLimited_ReturnsFailureWithUsageLimited()
    {
        // Arrange — non-zero exit + UsageLimited parse result → Failure(UsageLimited)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new UsageLimitedParser(resetsAt);
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: "output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.UsageLimited>();
    }

    [Fact]
    public async Task WhenMrIsClosedAndCommitsQueryReturnsNotFound_ReturnsFailure()
    {
        // Arrange — Closed MR + NotFound on commits = definitively no commits → Failure
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch closedMr = new(MergeRequestPresence.Closed, null);
        Error notFoundError = new Error("Provider.BranchNotFound", "Branch not found") { Kind = ErrorKind.NotFound };
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Fail(notFoundError),
            mrResults: [Result<MergeRequestByBranch>.Ok(closedMr)]);
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    // -------------------------------------------------------------------------
    // TransientApiError parse result wiring
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenNoMrAndNonZeroExitWithNoCommitsAndTransientApiError_ReturnsFailureWithTransientApiError()
    {
        // Arrange — non-zero exit + TransientApiError parse result + no commits → Failure(TransientApiError)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new TransientApiErrorParser();
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: "transient output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.TransientApiError>();
    }

    [Fact]
    public async Task WhenNoMrAndNonZeroExitWithCommitsAndTransientApiError_ReturnsContinuableFailureWithTransientApiError()
    {
        // Arrange — non-zero exit + TransientApiError parse result + commits exist → ContinuableFailure(TransientApiError)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new TransientApiErrorParser();
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: "transient output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.ContinuableFailure failure = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.TransientApiError>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndTransientApiError_ReturnsFailureWithTransientApiError()
    {
        // Arrange — exit 0 + no commits + TransientApiError parse result → Failure(TransientApiError)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new TransientApiErrorParser();
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: "transient output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.TransientApiError>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndNonTransientParse_ReturnsUnchanged()
    {
        // Arrange — exit 0 + no commits + NormalExit (non-transient) → Unchanged (regression guard)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Unchanged>();
    }

    [Fact]
    public async Task WhenNoMrAndNonZeroExitWithNoCommitsAndNonTransientNonUsageLimitedParse_ReturnsFailureWithNonZeroExit()
    {
        // Arrange — non-zero exit + NormalExit parse result (non-transient, non-usage-limited) → Failure(NonZeroExit)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 2,
            containerOutput: null,
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        FailureReason.NonZeroExit nonZeroExit = failure.FailureReason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ExitCode.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // CreditsExhausted parse result wiring
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndCreditsExhausted_ReturnsFailureWithCreditsExhausted()
    {
        // Arrange — exit 0 + no commits + CreditsExhausted parse result → Failure(CreditsExhausted)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new CreditsExhaustedParser();
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 0,
            containerOutput: "credits output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.CreditsExhausted>();
    }

    [Fact]
    public async Task WhenNoMrAndNonZeroExitWithNoCommitsAndCreditsExhausted_ReturnsFailureWithCreditsExhausted()
    {
        // Arrange — non-zero exit + CreditsExhausted parse result + no commits → Failure(CreditsExhausted)
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        IContainerOutputParser parser = new CreditsExhaustedParser();
        WorkerOutcomeResolver sut = BuildResolver(queries, parser);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            exitCode: 1,
            containerOutput: "credits output",
            DefaultCooldownMinutes,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.CreditsExhausted>();
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scripted provider stub. MR queries consume from <paramref name="mrResults"/> in order,
    /// then repeat <paramref name="fallbackMrResult"/> indefinitely. Commits consume from
    /// <paramref name="commitsResults"/> in order, then repeat <paramref name="commitsResult"/>
    /// indefinitely.
    /// </summary>
    private sealed class ScriptedProviderQueries : IPostExitProviderQueries
    {
        private readonly Queue<Result<MergeRequestByBranch>> _mrResultQueue;
        private readonly Result<MergeRequestByBranch> _fallbackMrResult;
        private readonly Queue<Result<bool>> _commitsResultQueue;
        private readonly Result<bool> _commitsResult;

        public ScriptedProviderQueries(
            Result<bool> commitsResult,
            Result<MergeRequestByBranch>? fallbackMrResult = null,
            Result<MergeRequestByBranch>[]? mrResults = null,
            Result<bool>[]? commitsResults = null)
        {
            _commitsResult = commitsResult;
            _fallbackMrResult = fallbackMrResult
                ?? Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null));
            _mrResultQueue = new Queue<Result<MergeRequestByBranch>>(mrResults ?? []);
            _commitsResultQueue = new Queue<Result<bool>>(commitsResults ?? []);
        }

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            Result<MergeRequestByBranch> result = _mrResultQueue.Count > 0
                ? _mrResultQueue.Dequeue()
                : _fallbackMrResult;
            return Task.FromResult(result);
        }

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            Result<bool> result = _commitsResultQueue.Count > 0
                ? _commitsResultQueue.Dequeue()
                : _commitsResult;
            return Task.FromResult(result);
        }

        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }

    private sealed class NullContainerOutputParser : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log)
            => new ContainerOutputParseResult.NormalExit();

        public RunResultSummary? ParseRunResultSummary(string? log) => null;
    }

    private sealed class UsageLimitedParser(DateTimeOffset resetsAt) : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log)
            => new ContainerOutputParseResult.UsageLimited(resetsAt);

        public RunResultSummary? ParseRunResultSummary(string? log) => null;
    }

    private sealed class NoResultLineParser : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log)
            => new ContainerOutputParseResult.NoResultLine();

        public RunResultSummary? ParseRunResultSummary(string? log) => null;
    }

    private sealed class TransientApiErrorParser : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log)
            => new ContainerOutputParseResult.TransientApiError();

        public RunResultSummary? ParseRunResultSummary(string? log) => null;
    }

    private sealed class CreditsExhaustedParser : IContainerOutputParser
    {
        public ContainerOutputParseResult Parse(string? log)
            => new ContainerOutputParseResult.CreditsExhausted();

        public RunResultSummary? ParseRunResultSummary(string? log) => null;
    }
}
