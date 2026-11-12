using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

[JsonDerivedType(typeof(NonZeroExit), typeDiscriminator: FailureReason.NonZeroExitToken)]
[JsonDerivedType(typeof(TimedOut), typeDiscriminator: FailureReason.TimedOutToken)]
[JsonDerivedType(typeof(ContainerError), typeDiscriminator: FailureReason.ContainerErrorToken)]
[JsonDerivedType(typeof(UsageLimited), typeDiscriminator: FailureReason.UsageLimitedToken)]
[JsonDerivedType(typeof(WorkerBootstrapFailed), typeDiscriminator: FailureReason.WorkerBootstrapFailedToken)]
[JsonDerivedType(typeof(AuthInvalid), typeDiscriminator: FailureReason.AuthInvalidToken)]
[JsonDerivedType(typeof(ProviderError), typeDiscriminator: FailureReason.ProviderErrorToken)]
[JsonDerivedType(typeof(TransientApiError), typeDiscriminator: FailureReason.TransientApiErrorToken)]
[JsonDerivedType(typeof(CreditsExhausted), typeDiscriminator: FailureReason.CreditsExhaustedToken)]
[JsonDerivedType(typeof(ModelUnavailable), typeDiscriminator: FailureReason.ModelUnavailableToken)]
[JsonDerivedType(typeof(Cancelled), typeDiscriminator: FailureReason.CancelledToken)]
[JsonDerivedType(typeof(TurnLimitReached), typeDiscriminator: FailureReason.TurnLimitToken)]
[JsonDerivedType(typeof(VerdictInvalid), typeDiscriminator: FailureReason.VerdictInvalidToken)]
[JsonDerivedType(typeof(PublishFailed), typeDiscriminator: FailureReason.PublishFailedToken)]
[JsonDerivedType(typeof(PublishExpired), typeDiscriminator: FailureReason.PublishExpiredToken)]
[JsonDerivedType(typeof(RunDiscarded), typeDiscriminator: FailureReason.RunDiscardedToken)]
public abstract record FailureReason
{
    public const string NonZeroExitToken = FailureCategory.NonZeroExitToken;
    public const string TimedOutToken = FailureCategory.TimedOutToken;
    public const string ContainerErrorToken = FailureCategory.ContainerErrorToken;
    public const string UsageLimitedToken = FailureCategory.UsageLimitedToken;
    public const string WorkerBootstrapFailedToken = FailureCategory.WorkerBootstrapFailedToken;
    public const string AuthInvalidToken = FailureCategory.AuthInvalidToken;
    public const string ProviderErrorToken = FailureCategory.ProviderErrorToken;
    public const string TransientApiErrorToken = FailureCategory.TransientApiErrorToken;
    public const string CreditsExhaustedToken = FailureCategory.CreditsExhaustedToken;
    public const string ModelUnavailableToken = FailureCategory.ModelUnavailableToken;
    public const string CancelledToken = FailureCategory.CancelledToken;
    public const string TurnLimitToken = FailureCategory.TurnLimitToken;
    public const string VerdictInvalidToken = FailureCategory.VerdictInvalidToken;
    public const string PublishFailedToken = FailureCategory.PublishFailedToken;
    public const string PublishExpiredToken = FailureCategory.PublishExpiredToken;
    public const string RunDiscardedToken = FailureCategory.RunDiscardedToken;

    private FailureReason()
    {
    }

    public string CategoryToken => this switch
    {
        NonZeroExit => NonZeroExitToken,
        TimedOut => TimedOutToken,
        ContainerError => ContainerErrorToken,
        UsageLimited => UsageLimitedToken,
        WorkerBootstrapFailed => WorkerBootstrapFailedToken,
        AuthInvalid => AuthInvalidToken,
        ProviderError => ProviderErrorToken,
        TransientApiError => TransientApiErrorToken,
        CreditsExhausted => CreditsExhaustedToken,
        ModelUnavailable => ModelUnavailableToken,
        Cancelled => CancelledToken,
        TurnLimitReached => TurnLimitToken,
        VerdictInvalid => VerdictInvalidToken,
        PublishFailed => PublishFailedToken,
        PublishExpired => PublishExpiredToken,
        RunDiscarded => RunDiscardedToken,
        _ => throw new UnreachableException($"Unknown {nameof(FailureReason)} variant: {GetType().Name}"),
    };

    public string Summary => this switch
    {
        NonZeroExit { Detail: null } nonZeroExit => $"Non-zero exit code: {nonZeroExit.ExitCode}",
        NonZeroExit nonZeroExit => $"Non-zero exit code: {nonZeroExit.ExitCode}: {nonZeroExit.Detail}",
        TimedOut => "Worker run timed out",
        ContainerError containerError => $"Container error: {containerError.Message}",
        UsageLimited => "Usage limit reached",
        WorkerBootstrapFailed bootstrapFailed => $"Worker bootstrap failed: {bootstrapFailed.Detail}",
        AuthInvalid => "Worker authentication failed",
        ProviderError providerError => providerError.Message,
        TransientApiError => "Transient Anthropic API fault",
        CreditsExhausted => "Credits exhausted",
        ModelUnavailable modelUnavailable =>
            $"The selected model is unavailable to this account. Pick another model in the '{modelUnavailable.Role}' worker role's model setting",
        Cancelled => "Worker run cancelled",
        TurnLimitReached => "Reached the turn limit before finishing. Split the issue into smaller issues, then retry.",
        VerdictInvalid verdictInvalid => verdictInvalid.Detail,
        PublishFailed publishFailed => $"Publishing the run's commits failed: {publishFailed.Detail}",
        PublishExpired => "The run's unpublished commits were discarded: they were not published within the Hand-off volume's time to live",
        RunDiscarded => "The run was discarded: its unpublished commits were deleted and nothing was pushed",
        _ => throw new UnreachableException($"Unknown {nameof(FailureReason)} variant: {GetType().Name}"),
    };

    /// <summary>
    /// The container exited non-zero. <see cref="Detail"/> carries what the CLI reported in an <c>is_error</c>
    /// result line, so the failure names its cause; reasons persisted before it existed have none.
    /// </summary>
    public sealed record NonZeroExit(int ExitCode, string? Detail = null) : FailureReason;

    public sealed record TimedOut : FailureReason;

    public sealed record ContainerError(string Message) : FailureReason;

    public sealed record UsageLimited(DateTimeOffset ResetsAt) : FailureReason;

    public sealed record WorkerBootstrapFailed(string Detail) : FailureReason;

    public sealed record AuthInvalid : FailureReason;

    public sealed record ProviderError(string Message) : FailureReason;

    public sealed record TransientApiError : FailureReason;

    public sealed record CreditsExhausted : FailureReason;

    /// <summary>
    /// The CLI rejected the run's model on its first request ("There's an issue with the selected model"). The model
    /// comes from the role's model setting, so <see cref="Role"/> names the setting to change.
    /// </summary>
    public sealed record ModelUnavailable(string Role) : FailureReason;

    /// <summary>
    /// A user cancelled the run from the dashboard; the container was stopped.
    /// </summary>
    public sealed record Cancelled : FailureReason;

    /// <summary>
    /// The CLI exhausted its <c>--max-turns</c> budget before finishing. The fix is to split the issue, so retry
    /// stays manual rather than repeating a full run's cost.
    /// </summary>
    public sealed record TurnLimitReached : FailureReason;

    /// <summary>
    /// A run whose role decides by its verdict (ADR 0094) exited 0 without a usable one: the verdict was missing from
    /// its output or failed the role's schema. A step failure, never a fallback to facts.
    /// </summary>
    public sealed record VerdictInvalid(string Detail) : FailureReason
    {
        public const string MissingDetail = "Verdict missing from output";

        public const string InvalidDetail = "Verdict failed the role's schema";

        public const string NoSuppliedSetDetail = "Blocked verdict cannot be checked: the run recorded no supplied open issues";

        public static VerdictInvalid BlockerNotSupplied(int issueNumber) =>
            new($"Blocked verdict names issue #{issueNumber}, which is not an open issue supplied to the run");

        public static VerdictInvalid BlockersExceedCap(int maxBlockers) =>
            new($"Blocked verdict names more than {maxBlockers} blockers");

        public static VerdictInvalid BlockerReasonTooLong(int issueNumber, int maxLength) =>
            new($"Blocked verdict's reason for issue #{issueNumber} is longer than {maxLength} characters");

        public static VerdictInvalid BlockerDuplicated(int issueNumber) =>
            new($"Blocked verdict names issue #{issueNumber} more than once");
    }

    /// <summary>
    /// Publishing the run's commits failed (ADR 0100), permanently or after every retry. The run keeps its Hand-off
    /// volume, so Retry publish can run it again.
    /// </summary>
    public sealed record PublishFailed(string Detail) : FailureReason;

    /// <summary>The run's Hand-off volume reached its time to live before its commits were published.</summary>
    public sealed record PublishExpired : FailureReason;

    /// <summary>An operator discarded a run holding unpublished work: its commits were deleted and nothing was pushed.</summary>
    public sealed record RunDiscarded : FailureReason;
}
