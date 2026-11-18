using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Modules.Workers.Features.Verdicts;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Resolves a review run (ADR 0113) from its exit and its verdict alone, as triage's are: a failed exit is classified as
/// for every run, and a missing or schema-invalid verdict is a step failure (ADR 0094). A parsed verdict must then pass
/// its checks against the facts the run was supplied at dispatch (ADR 0114); code, never the reviewer, derives its
/// outcome.
/// </summary>
internal sealed class ReviewOutcomeResolver(
    IContainerOutputParser containerOutputParser,
    IWorkerRoleCatalogStore roleCatalogStore) : IVerdictOutcomeResolver
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

        if (ReviewVerdictParser.TryParse(structuredOutput) is not { } verdict)
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

        return ReviewDecision.Decide(verdict, facts, PrincipleIdsOf(run.Role)) switch
        {
            ReviewDecisionResult.Decided decided => new WorkerOutcome.ReviewCompleted(decided.Decision, containerOutput, summary),
            ReviewDecisionResult.Rejected rejected =>
                new WorkerOutcome.Failure(rejected.Reason, containerOutput, summary, RunVerdict.Invalid()),
            _ => throw new System.Diagnostics.UnreachableException("Unknown review decision result."),
        };
    }

    // Before the first catalog loads no id is known, so a principle citation fails closed rather than passing unchecked.
    private HashSet<string> PrincipleIdsOf(WorkerRole role) =>
        roleCatalogStore.Current is { } catalog
            ? [.. catalog.Content.Principles.IdsFor(role.PrincipleSections)]
            : [];
}
