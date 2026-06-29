using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Domain;

[JsonDerivedType(typeof(NonZeroExit), typeDiscriminator: "non_zero_exit")]
[JsonDerivedType(typeof(TimedOut), typeDiscriminator: "timed_out")]
[JsonDerivedType(typeof(ContainerError), typeDiscriminator: "container_error")]
[JsonDerivedType(typeof(UsageLimited), typeDiscriminator: "usage_limited")]
[JsonDerivedType(typeof(WorkerBootstrapFailed), typeDiscriminator: "worker_bootstrap_failed")]
public abstract record FailureReason
{
    private FailureReason()
    {
    }

    public string CategoryToken => this switch
    {
        NonZeroExit => "non_zero_exit",
        TimedOut => "timed_out",
        ContainerError => "container_error",
        UsageLimited => "usage_limited",
        WorkerBootstrapFailed => "worker_bootstrap_failed",
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
