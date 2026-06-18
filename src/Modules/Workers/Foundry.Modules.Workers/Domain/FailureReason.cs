using System;
using System.Text.Json.Serialization;

namespace Foundry.Modules.Workers.Domain;

[JsonDerivedType(typeof(NonZeroExit), typeDiscriminator: "non_zero_exit")]
[JsonDerivedType(typeof(TimedOut), typeDiscriminator: "timed_out")]
[JsonDerivedType(typeof(ContainerError), typeDiscriminator: "container_error")]
[JsonDerivedType(typeof(UsageLimited), typeDiscriminator: "usage_limited")]
public abstract record FailureReason
{
    private FailureReason()
    {
    }

    public sealed record NonZeroExit(int ExitCode) : FailureReason;

    public sealed record TimedOut : FailureReason;

    public sealed record ContainerError(string Message) : FailureReason;

    public sealed record UsageLimited(DateTimeOffset ResetsAt) : FailureReason;
}
