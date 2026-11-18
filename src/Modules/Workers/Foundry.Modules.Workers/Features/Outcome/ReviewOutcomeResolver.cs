using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Resolves a review run (ADR 0113) fail-closed until the review verdict is parsed and checked (#1078): an exit that
/// failed is classified as for every run, and any other run fails as <c>verdict_invalid</c>, so a review can never
/// approve anything. Nothing dispatches a review yet, so this resolver never runs in production.
/// </summary>
internal sealed class ReviewOutcomeResolver(IContainerOutputParser containerOutputParser) : IVerdictOutcomeResolver
{
    public const string NotReadDetail = "Review verdicts are not read yet";

    public WorkerRole Role => WorkerRole.Review;

    public WorkerOutcome Resolve(IResolvableRun run, WorkerStatus? status, string? containerOutput)
    {
        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        if (!RunFailureClassifier.Succeeded(status, summary))
        {
            return new WorkerOutcome.Failure(
                RunFailureClassifier.Classify(containerOutputParser, status, containerOutput, summary, run.Role),
                containerOutput,
                summary);
        }

        return new WorkerOutcome.Failure(new FailureReason.VerdictInvalid(NotReadDetail), containerOutput, summary);
    }
}
