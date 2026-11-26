using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared.Infrastructure.Docker;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Classifies how a run ended, shared by the branch path of <see cref="WorkerOutcomeResolver"/> and every
/// <see cref="IVerdictOutcomeResolver"/> so all roles classify a failure identically.
/// </summary>
internal static class RunFailureClassifier
{
    private const string HeuristicBootstrapDetail =
        "stage=unknown (no result line; container terminated before producing output)";

    // SIGKILL (128 + 9) and SIGTERM (128 + 15): a docker kill/stop issued outside Foundry.
    private const int SigKillExitCode = 137;

    private const int SigTermExitCode = 143;

    /// <summary>
    /// Whether the run succeeded: its container exited 0 and its result line does not report <c>is_error</c>.
    /// The CLI can exit 0 while reporting <c>is_error</c> (e.g. an expired OAuth session); such a run did no work.
    /// </summary>
    public static bool Succeeded(WorkerStatus? status, RunResultSummary? summary)
        => status?.ExitCode == 0 && summary is not { IsError: true };

    /// <summary>
    /// Classifies a run that did not succeed. A specific category from the output wins, so a usage, credit or auth
    /// failure is never reported as a generic error.
    /// </summary>
    public static FailureReason Classify(
        IContainerOutputParser containerOutputParser,
        WorkerStatus? status,
        string? containerOutput,
        RunResultSummary? summary,
        IResolvableRun run)
    {
        ContainerOutputParseResult parseResult = containerOutputParser.Parse(containerOutput);

        if (TryClassify(parseResult, run) is { } classified)
        {
            return classified;
        }

        string? errorText = summary is { IsError: true } ? RedactedResultText(summary) : null;

        if (status?.ExitCode == 0)
        {
            return new FailureReason.ContainerError(
                errorText is null
                    ? WorkerOutcomeResolver.ErrorResultMessage
                    : $"{WorkerOutcomeResolver.ErrorResultMessage}: {errorText}");
        }

        return parseResult switch
        {
            ContainerOutputParseResult.NoResultLine => ClassifyNoResultLine(status, agentStarted: false),
            ContainerOutputParseResult.AgentStartedNoResultLine => ClassifyNoResultLine(status, agentStarted: true),
            _ => new FailureReason.NonZeroExit(status?.ExitCode ?? -1, errorText),
        };
    }

    /// <summary>
    /// Maps a parse result that identifies a specific failure category to its
    /// <see cref="FailureReason"/>; returns <see langword="null"/> when the parse carries no category.
    /// Shared by the non-zero and the exit-0 paths so both classify a failure identically.
    /// </summary>
    public static FailureReason? TryClassify(ContainerOutputParseResult parseResult, IResolvableRun run)
    {
        return parseResult switch
        {
            ContainerOutputParseResult.WorkerBootstrapFailed bootstrapFailed =>
                new FailureReason.WorkerBootstrapFailed(SecretRedactor.Redact(bootstrapFailed.Detail)),
            ContainerOutputParseResult.PackageSourceUnreachable packageSourceUnreachable =>
                new FailureReason.PackageSourceUnreachable(SecretRedactor.Redact(packageSourceUnreachable.Detail)),
            ContainerOutputParseResult.PackageRestoreFailed packageRestoreFailed =>
                new FailureReason.PackageRestoreFailed(SecretRedactor.Redact(packageRestoreFailed.Detail)),
            ContainerOutputParseResult.UsageLimited usageLimited => new FailureReason.UsageLimited(usageLimited.ResetsAt),
            ContainerOutputParseResult.CreditsExhausted => new FailureReason.CreditsExhausted(),
            ContainerOutputParseResult.AuthInvalid => new FailureReason.AuthInvalid(),
            ContainerOutputParseResult.TransientApiError => new FailureReason.TransientApiError(),
            ContainerOutputParseResult.ModelUnavailable => ModelUnavailable(run),
            ContainerOutputParseResult.TurnLimitReached => new FailureReason.TurnLimitReached(),
            _ => null,
        };
    }

    // An implement run names the Tier Model setting it used, read as demanding when the run recorded no demand, as
    // dispatch reads a claim without one.
    private static FailureReason.ModelUnavailable ModelUnavailable(IResolvableRun run) =>
        run.Role == WorkerRole.Implement
            ? new FailureReason.ModelUnavailable(run.Role.Name, run.Demand ?? ImplementDemand.Demanding)
            : new FailureReason.ModelUnavailable(run.Role.Name);

    /// <summary>
    /// The result line's text made safe to persist as a failure reason: secrets redacted, one line, and bounded
    /// by <see cref="WorkerOutcomeResolver.MaxErrorResultTextLength"/>. <see langword="null"/> when the result line has no text.
    /// </summary>
    private static string? RedactedResultText(RunResultSummary summary)
    {
        string? resultText = summary.ResultText?.Trim();

        if (string.IsNullOrEmpty(resultText))
        {
            return null;
        }

        string redacted = SecretRedactor.Redact(resultText).ReplaceLineEndings(" ");

        return redacted.Length > WorkerOutcomeResolver.MaxErrorResultTextLength
            ? redacted[..WorkerOutcomeResolver.MaxErrorResultTextLength]
            : redacted;
    }

    /// <summary>
    /// Classifies a run that ended without a result line. Containers killed or removed outside
    /// Foundry are container errors. Otherwise a run whose agent started is a plain non-zero exit,
    /// and one whose agent never started is a bootstrap failure.
    /// </summary>
    private static FailureReason ClassifyNoResultLine(WorkerStatus? status, bool agentStarted)
    {
        if (status is null)
        {
            return new FailureReason.ContainerError(WorkerOutcomeResolver.RemovedOutsideFoundryMessage);
        }

        // OOM is checked first: the kernel's OOM kill also surfaces as exit 137.
        if (status.OomKilled)
        {
            return new FailureReason.ContainerError(WorkerOutcomeResolver.OutOfMemoryMessage);
        }

        if (status.ExitCode is SigKillExitCode or SigTermExitCode)
        {
            return new FailureReason.ContainerError(WorkerOutcomeResolver.TerminatedOutsideFoundryMessage);
        }

        return agentStarted
            ? new FailureReason.NonZeroExit(status.ExitCode ?? -1)
            : new FailureReason.WorkerBootstrapFailed(HeuristicBootstrapDetail);
    }
}
