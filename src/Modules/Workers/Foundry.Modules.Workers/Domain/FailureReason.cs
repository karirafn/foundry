using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Domain;

[JsonDerivedType(typeof(NonZeroExit), typeDiscriminator: FailureReason.NonZeroExitToken)]
[JsonDerivedType(typeof(TimedOut), typeDiscriminator: FailureReason.TimedOutToken)]
[JsonDerivedType(typeof(ContainerError), typeDiscriminator: FailureReason.ContainerErrorToken)]
[JsonDerivedType(typeof(UsageLimited), typeDiscriminator: FailureReason.UsageLimitedToken)]
[JsonDerivedType(typeof(WorkerBootstrapFailed), typeDiscriminator: FailureReason.WorkerBootstrapFailedToken)]
public abstract record FailureReason
{
    public const string NonZeroExitToken = "non_zero_exit";
    public const string TimedOutToken = "timed_out";
    public const string ContainerErrorToken = "container_error";
    public const string UsageLimitedToken = "usage_limited";
    public const string WorkerBootstrapFailedToken = "worker_bootstrap_failed";

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
        _ => throw new UnreachableException($"Unknown {nameof(FailureReason)} variant: {GetType().Name}"),
    };

    public string Summary => this switch
    {
        NonZeroExit nonZeroExit => $"Non-zero exit code: {nonZeroExit.ExitCode}",
        TimedOut => "Worker run timed out",
        ContainerError containerError => $"Container error: {containerError.Message}",
        UsageLimited => "Usage limit reached",
        WorkerBootstrapFailed bootstrapFailed => $"Worker bootstrap failed: {bootstrapFailed.Detail}",
        _ => throw new UnreachableException($"Unknown {nameof(FailureReason)} variant: {GetType().Name}"),
    };

    public sealed record NonZeroExit(int ExitCode) : FailureReason;

    public sealed record TimedOut : FailureReason;

    public sealed record ContainerError(string Message) : FailureReason;

    public sealed record UsageLimited(DateTimeOffset ResetsAt) : FailureReason;

    public sealed record WorkerBootstrapFailed(string Detail) : FailureReason;
}
