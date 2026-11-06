using System.Diagnostics;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

public sealed class ResolveAsync
{
    private const string DefaultBranchName = "feat/1-my-feature";
    private const string PrUrl = "https://github.com/owner/repo/pull/42";

    private static ActiveRun CreateActiveRun(string branchName = DefaultBranchName)
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        return starting.Activate(
            ContainerId.From("container-123"),
            BranchName.From(branchName),
            MonitoredRepositoryId.New());
    }

    private static WorkerStatus Exited(int exitCode, bool oomKilled = false)
        => new(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow, OomKilled: oomKilled);

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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(1),
            containerOutput: null,
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
            status: Exited(1),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: "some output",
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
            status: Exited(0),
            containerOutput: "usage limit output",
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
            status: Exited(1),
            containerOutput: null,
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
            status: Exited(1),
            containerOutput: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>();
    }

    [Fact]
    public async Task WhenNoMrAndNullExitWithNoCommits_ReturnsFailure()
    {
        // Arrange — container not found (null status) is classified as a failure
        ActiveRun run = CreateActiveRun();
        MergeRequestByBranch noneMr = new(MergeRequestPresence.None, null);
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(false),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(noneMr));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            status: null,
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(1),
            containerOutput: null,
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
            status: Exited(1),
            containerOutput: "output",
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
            status: Exited(1),
            containerOutput: null,
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
            status: Exited(1),
            containerOutput: "transient output",
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
            status: Exited(1),
            containerOutput: "transient output",
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
            status: Exited(0),
            containerOutput: "transient output",
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
            status: Exited(0),
            containerOutput: null,
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
            status: Exited(2),
            containerOutput: null,
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
            status: Exited(0),
            containerOutput: "credits output",
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
            status: Exited(1),
            containerOutput: "credits output",
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.CreditsExhausted>();
    }

    // -------------------------------------------------------------------------
    // Model-unavailable result line: a named reason pointing at the role's model setting
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task WhenNoMrAndNoCommitsAndModelUnavailable_ReturnsFailureNamingTheRole(int exitCode)
    {
        // Arrange
        ActiveRun run = CreateActiveRun();
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));
        string log = """
            {"type":"result","subtype":"success","is_error":true,"api_error_status":404,"result":"There's an issue with the selected model (claude-x). It may not exist or you may not have access to it. Run /model to pick a different model."}
            """;

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            run,
            status: Exited(exitCode),
            containerOutput: log,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        FailureReason.ModelUnavailable reason = failure.FailureReason.ShouldBeOfType<FailureReason.ModelUnavailable>();
        reason.Role.ShouldBe(run.Role.Name);
    }

    // -------------------------------------------------------------------------
    // External-kill classification (no result line) — real parser so the
    // no-result-line path is observable.
    // -------------------------------------------------------------------------

    private static WorkerOutcomeResolver BuildResolverWithRealParser(IPostExitProviderQueries providerQueries)
        => BuildResolver(providerQueries, new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance));

    private static IPostExitProviderQueries NoMrQueries(bool hasCommits)
        => new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(hasCommits),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

    // -------------------------------------------------------------------------
    // Exit 0 with an is_error result line — captured stream-json result-line fixtures
    // -------------------------------------------------------------------------

    private const string ErrorResultText = "Execution error: the session ended before the task could be completed";

    private const string IsErrorResultLine =
        """{"type":"result","subtype":"success","is_error":true,"duration_ms":412,"duration_api_ms":0,"num_turns":1,"result":"Execution error: the session ended before the task could be completed","session_id":"9b0c2f1e-4d7a-4c1b-8f3e-2a6d5e7c9b10","total_cost_usd":0,"usage":{"input_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":0}}""";

    // Real token-refresh failure: no api_error_status and no error object, only is_error + result text.
    private const string FailedToAuthenticateIsErrorResultLine =
        """{"type":"result","subtype":"success","is_error":true,"num_turns":1,"api_error_status":null,"duration_ms":117,"result":"Failed to authenticate: OAuth session expired and could not be refreshed"}""";

    private const string SuccessResultLine =
        """{"type":"result","subtype":"success","is_error":false,"duration_ms":81234,"duration_api_ms":79001,"num_turns":12,"result":"No changes were needed; the issue is already resolved.","session_id":"9b0c2f1e-4d7a-4c1b-8f3e-2a6d5e7c9b10","total_cost_usd":0.4213,"usage":{"input_tokens":12,"cache_creation_input_tokens":3400,"cache_read_input_tokens":51000,"output_tokens":900}}""";

    private const string AuthInvalidIsErrorResultLine =
        """{"type":"result","subtype":"success","is_error":true,"api_error_status":401,"duration_ms":390,"duration_api_ms":120,"num_turns":1,"result":"Failed to authenticate. API Error: 401 {\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"OAuth token has expired.\"}}","session_id":"9b0c2f1e-4d7a-4c1b-8f3e-2a6d5e7c9b10","total_cost_usd":0}""";

    private const string OverloadedIsErrorResultLine =
        """{"type":"result","subtype":"success","is_error":true,"duration_ms":5120,"duration_api_ms":5000,"num_turns":3,"result":"API Error: 529 Overloaded","session_id":"9b0c2f1e-4d7a-4c1b-8f3e-2a6d5e7c9b10","total_cost_usd":0.01}""";

    private static string ContainerLog(string resultLine)
        => $$"""
            {"type":"system","subtype":"init","session_id":"9b0c2f1e-4d7a-4c1b-8f3e-2a6d5e7c9b10"}
            {{resultLine}}
            """;

    private static async Task<WorkerOutcome> ResolveExitZeroNoCommitsAsync(string containerOutput)
    {
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        return await sut.ResolveAsync(
            CreateActiveRun(),
            status: Exited(0),
            containerOutput,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndIsErrorResultLine_ReturnsFailureCarryingResultText()
    {
        // Arrange
        string containerOutput = ContainerLog(IsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveExitZeroNoCommitsAsync(containerOutput);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        FailureReason.ContainerError containerError = failure.FailureReason.ShouldBeOfType<FailureReason.ContainerError>();
        containerError.Message.ShouldBe($"{WorkerOutcomeResolver.ErrorResultMessage}: {ErrorResultText}");
        failure.ContainerOutput.ShouldBe(containerOutput);
        RunResultSummary summary = failure.Summary.ShouldNotBeNull();
        summary.IsError.ShouldBeTrue();
        summary.ResultText.ShouldBe(ErrorResultText);
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndIsErrorFalseResultLine_ReturnsUnchanged()
    {
        // Arrange
        string containerOutput = ContainerLog(SuccessResultLine);

        // Act
        WorkerOutcome outcome = await ResolveExitZeroNoCommitsAsync(containerOutput);

        // Assert
        WorkerOutcome.Unchanged unchanged = outcome.ShouldBeOfType<WorkerOutcome.Unchanged>();
        unchanged.Summary.ShouldNotBeNull().IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndAuthInvalidIsErrorResultLine_ReturnsFailureWithAuthInvalid()
    {
        // Arrange
        string containerOutput = ContainerLog(AuthInvalidIsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveExitZeroNoCommitsAsync(containerOutput);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.AuthInvalid>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndFailedToAuthenticateResultLine_ReturnsFailureWithAuthInvalid()
    {
        // Arrange
        string containerOutput = ContainerLog(FailedToAuthenticateIsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveExitZeroNoCommitsAsync(containerOutput);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.AuthInvalid>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndOverloadedIsErrorResultLine_ReturnsFailureWithTransientApiError()
    {
        // Arrange
        string containerOutput = ContainerLog(OverloadedIsErrorResultLine);

        // Act
        WorkerOutcome outcome = await ResolveExitZeroNoCommitsAsync(containerOutput);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.TransientApiError>();
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndIsErrorResultLineWithoutText_ReturnsGenericFailure()
    {
        // Arrange
        string containerOutput = ContainerLog("""{"type":"result","subtype":"error_during_execution","is_error":true,"num_turns":0}""");

        // Act
        WorkerOutcome outcome = await ResolveExitZeroNoCommitsAsync(containerOutput);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBe(new FailureReason.ContainerError(WorkerOutcomeResolver.ErrorResultMessage));
    }

    [Fact]
    public async Task WhenNoMrAndExitZeroWithNoCommitsAndLongMultilineIsErrorText_TruncatesToSingleLine()
    {
        // Arrange
        string longText = "first line\\n" + new string('x', WorkerOutcomeResolver.MaxErrorResultTextLength * 2);
        string containerOutput = ContainerLog($$"""{"type":"result","is_error":true,"result":"{{longText}}"}""");

        // Act
        WorkerOutcome outcome = await ResolveExitZeroNoCommitsAsync(containerOutput);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        FailureReason.ContainerError containerError = failure.FailureReason.ShouldBeOfType<FailureReason.ContainerError>();
        string detail = containerError.Message[(WorkerOutcomeResolver.ErrorResultMessage.Length + 2)..];
        detail.Length.ShouldBe(WorkerOutcomeResolver.MaxErrorResultTextLength);
        detail.ShouldStartWith("first line x");
        detail.ShouldNotContain('\n');
    }

    [Fact]
    public async Task WhenOomKilledWithNoResultLine_ReturnsOutOfMemoryContainerError()
    {
        // Arrange — OOM also exits 137, so the OOM flag must win over the exit code
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            status: Exited(137, oomKilled: true),
            containerOutput: "cloning repository...",
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBe(new FailureReason.ContainerError("Worker ran out of memory"));
    }

    [Theory]
    [InlineData(137)]
    [InlineData(143)]
    public async Task WhenKilledOutsideFoundryWithNoResultLine_ReturnsTerminatedContainerError(int exitCode)
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            status: Exited(exitCode),
            containerOutput: "cloning repository...",
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBe(new FailureReason.ContainerError("Worker container was terminated outside Foundry"));
    }

    [Fact]
    public async Task WhenKilledOutsideFoundryWithCommits_ReturnsContinuableFailure()
    {
        // Arrange — commits still decide ContinuableFailure vs Failure
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: true));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            status: Exited(137),
            containerOutput: null,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.ContinuableFailure continuable = outcome.ShouldBeOfType<WorkerOutcome.ContinuableFailure>();
        continuable.FailureReason.ShouldBe(new FailureReason.ContainerError("Worker container was terminated outside Foundry"));
    }

    [Fact]
    public async Task WhenContainerNotFound_ReturnsRemovedContainerError()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            status: null,
            containerOutput: null,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBe(new FailureReason.ContainerError("Worker container was removed outside Foundry"));
    }

    [Fact]
    public async Task WhenOtherNonZeroExitWithNoResultLine_ReturnsBootstrapFailed()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(NoMrQueries(hasCommits: false));

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            status: Exited(1),
            containerOutput: "cloning repository...",
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Failure failure = outcome.ShouldBeOfType<WorkerOutcome.Failure>();
        failure.FailureReason.ShouldBeOfType<FailureReason.WorkerBootstrapFailed>();
    }

    [Fact]
    public async Task WhenKilledWithOpenMr_ReturnsReview()
    {
        // Arrange — the MR-first check runs before any kill classification
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolverWithRealParser(queries);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateActiveRun(),
            status: Exited(137, oomKilled: true),
            containerOutput: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Review>();
    }

    // -------------------------------------------------------------------------
    // TryResolveExistingPullRequestAsync — used by the cancel path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryResolveExistingPullRequest_WhenOpenMr_ReturnsReview()
    {
        // Arrange
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Open, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome? outcome = await sut.TryResolveExistingPullRequestAsync(
            CreateActiveRun(),
            summary: null,
            TestContext.Current.CancellationToken);

        // Assert
        WorkerOutcome.Review review = outcome.ShouldBeOfType<WorkerOutcome.Review>();
        review.PullRequestUrl.Value.ShouldBe(PrUrl);
    }

    [Fact]
    public async Task TryResolveExistingPullRequest_WhenMergedMr_ReturnsCompleted()
    {
        // Arrange
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.Merged, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome? outcome = await sut.TryResolveExistingPullRequestAsync(
            CreateActiveRun(),
            summary: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Completed>();
    }

    [Theory]
    [InlineData(MergeRequestPresence.None)]
    [InlineData(MergeRequestPresence.Closed)]
    public async Task TryResolveExistingPullRequest_WhenNoOpenOrMergedMr_ReturnsNull(MergeRequestPresence presence)
    {
        // Arrange
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(presence, PrUrl)));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome? outcome = await sut.TryResolveExistingPullRequestAsync(
            CreateActiveRun(),
            summary: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeNull();
    }

    [Fact]
    public async Task TryResolveExistingPullRequest_WhenLookupFails_ReturnsNull()
    {
        // Arrange — a transient lookup failure is treated as "no PR"
        IPostExitProviderQueries queries = new ScriptedProviderQueries(
            commitsResult: Result<bool>.Ok(true),
            fallbackMrResult: Result<MergeRequestByBranch>.Fail(new Error("Provider.Unavailable", "boom")));
        WorkerOutcomeResolver sut = BuildResolver(queries);

        // Act
        WorkerOutcome? outcome = await sut.TryResolveExistingPullRequestAsync(
            CreateActiveRun(),
            summary: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeNull();
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

        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            Result<bool> boolResult = _commitsResultQueue.Count > 0
                ? _commitsResultQueue.Dequeue()
                : _commitsResult;

            Result<BranchCommitSummary> result = boolResult switch
            {
                Result<bool>.Success { Value: true } => Result<BranchCommitSummary>.Ok(new BranchCommitSummary(1, "sha")),
                Result<bool>.Success { Value: false } => Result<BranchCommitSummary>.Ok(new BranchCommitSummary(0, null)),
                Result<bool>.Failure f => Result<BranchCommitSummary>.Fail(f.Error),
                _ => throw new UnreachableException(),
            };

            return Task.FromResult(result);
        }
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
