using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Verdicts;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Resolves a triage run (ADR 0097) from its exit and its verdict alone: it has no branch, so the merge-request
/// lookup never runs. Only a run that exited 0 with a schema-valid verdict completes; a missing or invalid verdict
/// is a step failure (ADR 0094), and any other exit is classified as for every run.
/// </summary>
internal sealed class TriageOutcomeResolver(IContainerOutputParser containerOutputParser) : IVerdictOutcomeResolver
{
    public WorkerRole Role => WorkerRole.Triage;

    public WorkerOutcome Resolve(IResolvableRun run, WorkerStatus? status, string? containerOutput)
    {
        RunResultSummary? summary = containerOutputParser.ParseRunResultSummary(containerOutput);

        if (!RunFailureClassifier.Succeeded(status, summary))
        {
            return new WorkerOutcome.Failure(
                RunFailureClassifier.Classify(containerOutputParser, status, containerOutput, summary, run),
                containerOutput,
                summary);
        }

        if (RunFailureClassifier.TryClassify(containerOutputParser.Parse(containerOutput), run) is { } classified)
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

        if (TriageVerdict.TryParse(structuredOutput) is not { } verdict)
        {
            return new WorkerOutcome.Failure(
                new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.InvalidDetail),
                containerOutput,
                summary);
        }

        return verdict is TriageVerdict.Blocked blocked
            && blocked.RejectionAgainst(run.SuppliedOpenIssueNumbers) is { } rejection
                ? new WorkerOutcome.Failure(rejection, containerOutput, summary, RunVerdict.Invalid())
                : new WorkerOutcome.TriageCompleted(verdict, containerOutput, summary);
    }
}
