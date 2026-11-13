using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;

internal static class PublishingRunFixture
{
    public static readonly DateTimeOffset ExitedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    public static readonly RunBase Base = new(
        CommitSha.From("1111111111111111111111111111111111111111"),
        CommitSha.From("2222222222222222222222222222222222222222"));

    public static readonly BranchName Branch = BranchName.From("feat/42-add-retry");

    public static ActiveRun ActiveRun(BranchName? branch = null)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, MonitoredRepositoryId.New());
        return starting.Activate(ContainerId.From("container-1"), branch ?? Branch, MonitoredRepositoryId.New());
    }

    public static PublishingRun Publishing(RunExit? exit = null, RunVerdict? verdict = null) =>
        ActiveRun().BeginPublishing(
            exit ?? new RunExit(0, OomKilled: false, RunStopReason.Exited, ExitedAt),
            containerOutput: "log tail",
            verdict ?? RunVerdict.Done(),
            new RunPullRequestText("Add retry", "Body"));
}
