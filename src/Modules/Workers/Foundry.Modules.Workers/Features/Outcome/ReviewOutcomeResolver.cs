using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Verdicts;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Resolves a review run (ADR 0113) from its exit and its verdict alone: it has no branch, so the merge-request lookup
/// never runs. A failed exit, a classified parse failure, and a missing or invalid verdict fail the run as for triage.
/// A verdict that matches the schema is then checked against the facts the run was supplied at dispatch and the
/// principle ids its role can cite; only a verdict that passes completes, with the outcome code derives from it.
/// </summary>
internal sealed class ReviewOutcomeResolver(
    IContainerOutputParser containerOutputParser,
    IWorkerRoleCatalogStore catalogStore) : IVerdictOutcomeResolver
{
    public const string NoSuppliedFactsDetail = "Review verdict cannot be checked: the run recorded no supplied review facts";

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

        if (RunFailureClassifier.TryClassify(containerOutputParser.Parse(containerOutput), run.Role) is { } classified)
        {
            return new WorkerOutcome.Failure(classified, containerOutput, summary);
        }

        if (containerOutputParser.ParseStructuredOutput(containerOutput) is not { } structuredOutput)
        {
            return new WorkerOutcome.Failure(
                new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.MissingDetail),
                containerOutput,
                summary);
        }

        if (ReviewVerdict.TryParse(structuredOutput) is not { } verdict)
        {
            return new WorkerOutcome.Failure(
                new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.InvalidDetail),
                containerOutput,
                summary);
        }

        if (run.SuppliedReviewFacts is not { } facts)
        {
            return new WorkerOutcome.Failure(
                new FailureReason.VerdictInvalid(NoSuppliedFactsDetail),
                containerOutput,
                summary,
                RunVerdict.Invalid());
        }

        return verdict.CheckAgainst(facts, PrincipleIdsOf(run.Role)) switch
        {
            ReviewVerdictCheck.Accepted accepted => new WorkerOutcome.ReviewCompleted(accepted.Verdict, containerOutput, summary),
            ReviewVerdictCheck.Rejected rejected =>
                new WorkerOutcome.Failure(rejected.Reason, containerOutput, summary, RunVerdict.Invalid()),
            _ => throw new System.Diagnostics.UnreachableException("Unknown review verdict check."),
        };
    }

    // Before the first image build after a start there is no catalog: no principle can be confirmed, so a verdict citing
    // one fails and the issue goes to a person (ADR 0113) rather than waiting on a build that may never complete.
    private HashSet<string> PrincipleIdsOf(WorkerRole role) =>
        catalogStore.Current is { } catalog
            ? new HashSet<string>(catalog.Content.Principles.IdsFor(role.PrincipleSections), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
}
