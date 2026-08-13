using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

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
public abstract record FailureReason
{
    public const string NonZeroExitToken = "non_zero_exit";
    public const string TimedOutToken = "timed_out";
    public const string ContainerErrorToken = "container_error";
    public const string UsageLimitedToken = "usage_limited";
    public const string WorkerBootstrapFailedToken = "worker_bootstrap_failed";
    public const string AuthInvalidToken = "auth_invalid";
    public const string ProviderErrorToken = "provider_error";
    public const string TransientApiErrorToken = "transient_api_error";
    public const string CreditsExhaustedToken = "credits_exhausted";

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
}
