using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

/// <summary>
/// Verifies that an exited run's verdict (ADR 0094) is read from its container output and recorded on the terminal
/// run, and that the outcome stays the fact-based one for every verdict status.
/// </summary>
public sealed class RunVerdictCapture : WorkerDispatchServiceTestBase
{
    private const string PullRequestUrlValue = "https://github.com/owner/repo/pull/1";

    private const string NeedsInputLog =
        """
        {"type":"system","subtype":"init","model":"claude-test"}
        {"type":"result","subtype":"success","is_error":false,"result":"Blocked","structured_output":{"outcome":"needs_input","questions":[{"question":"Which database?","context":"The issue names two."}]}}
        """;

    private const string DoneLog =
        """{"type":"result","subtype":"success","is_error":false,"result":"Done","structured_output":{"outcome":"done","pullRequest":{"summary":"add retry","body":"Adds retry."}}}""";

    private const string MissingLog =
        """{"type":"result","subtype":"success","is_error":false,"result":"Done"}""";

    private const string InvalidLog =
        """{"type":"result","subtype":"success","is_error":false,"result":"Done","structured_output":{"outcome":"finished"}}""";

    public static TheoryData<string, RunVerdictStatus> VerdictLogs => new()
    {
        { DoneLog, RunVerdictStatus.Done },
        { NeedsInputLog, RunVerdictStatus.NeedsInput },
        { MissingLog, RunVerdictStatus.Missing },
        { InvalidLog, RunVerdictStatus.Invalid },
    };

    [Theory]
    [MemberData(nameof(VerdictLogs))]
    public async Task WhenAnExitZeroRunHasAnOpenPullRequest_RecordsTheVerdictStatusAndKeepsTheReviewOutcome(
        string log,
        RunVerdictStatus expected)
    {
        // Arrange
        SeedActiveRun("container-verdict-review");
        DispatchAndPublishTicker sut = BuildVerdictService(
            exitCode: 0,
            log,
            new StubPostExitProviderQueries(
                hasCommits: true,
                mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, PullRequestUrlValue)));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        CompletedRun completed = run.ShouldBeOfType<CompletedRun>();
        completed.ShouldSatisfyAllConditions(
            () => completed.PullRequestUrl.ShouldNotBeNull().Value.ShouldBe(PullRequestUrlValue),
            () => completed.VerdictStatus.ShouldBe(expected));
    }

    [Fact]
    public async Task WhenANeedsInputRunIsRecorded_StoresItsQuestionsWithTheRun()
    {
        // Arrange
        SeedActiveRun("container-verdict-questions");
        DispatchAndPublishTicker sut = BuildVerdictService(
            exitCode: 0,
            NeedsInputLog,
            new StubPostExitProviderQueries(
                hasCommits: true,
                mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, PullRequestUrlValue)));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.VerdictQuestions.ShouldBe([new RunVerdictQuestion("Which database?", "The issue names two.")]);
    }

    [Theory]
    [MemberData(nameof(VerdictLogs))]
    public async Task WhenAnExitZeroRunHasNoCommits_RecordsTheVerdictStatusAndKeepsTheUnchangedOutcome(
        string log,
        RunVerdictStatus expected)
    {
        // Arrange
        SeedActiveRun("container-verdict-unchanged");
        DispatchAndPublishTicker sut = BuildVerdictService(exitCode: 0, log, new StubPostExitProviderQueries(hasCommits: false));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        CompletedRun completed = run.ShouldBeOfType<CompletedRun>();
        completed.ShouldSatisfyAllConditions(
            () => completed.PullRequestUrl.ShouldBeNull(),
            () => completed.VerdictStatus.ShouldBe(expected));
    }

    [Fact]
    public async Task WhenTheRunExitsNonZero_IgnoresTheVerdict()
    {
        // Arrange
        SeedActiveRun("container-verdict-failed");
        DispatchAndPublishTicker sut = BuildVerdictService(exitCode: 1, NeedsInputLog, new StubPostExitProviderQueries(hasCommits: false));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldSatisfyAllConditions(
            () => run.ShouldBeOfType<FailedRun>(),
            () => run.VerdictStatus.ShouldBeNull(),
            () => run.VerdictQuestions.ShouldBeNull());
    }

    private DispatchAndPublishTicker BuildVerdictService(int exitCode, string log, IPostExitProviderQueries queries)
    {
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: exitCode, FinishedAt: DateTimeOffset.UtcNow);

        return BuildService(
            new ExitedWorkerOrchestrator(exitedStatus, log),
            containerOutputParser: new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance),
            postExitProviderQueries: queries);
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus status, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in verdict tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(status));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult(logs);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
