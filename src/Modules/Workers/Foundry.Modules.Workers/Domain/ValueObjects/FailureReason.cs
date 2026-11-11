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
        _ => throw new UnreachableException($"Unknown {nameof(FailureReason)} variant: {GetType().Name}"),
    };

    public string Summary => this switch
    {
        NonZeroExit nonZeroExit => $"Non-zero exit code: {nonZeroExit.ExitCode}",
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
        _ => throw new UnreachableException($"Unknown {nameof(FailureReason)} variant: {GetType().Name}"),
    };

    public sealed record NonZeroExit(int ExitCode) : FailureReason;

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
    }
}
