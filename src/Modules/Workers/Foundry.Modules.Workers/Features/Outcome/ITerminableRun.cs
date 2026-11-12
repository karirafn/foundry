using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>A run the outcome applier can move to its terminal state: a running run, or a publishing one (ADR 0100).</summary>
internal interface ITerminableRun : IResolvableRun
{
    IssueId IssueId { get; }

    DateTimeOffset StartedAt { get; }

    CompletedRun Complete(
        int exitCode,
        BranchName? branchName,
        PullRequestUrl? pullRequestUrl,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null,
        string? containerOutput = null);

    FailedRun Fail(
        FailureReason reason,
        BranchName? branchNameOrNull,
        string? containerOutput = null,
        RunResultSummary? resultSummary = null,
        RunVerdict? verdict = null);
}
