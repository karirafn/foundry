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
