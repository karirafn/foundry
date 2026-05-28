using System.Text.Json.Serialization;

namespace Foundry.WebApi.Modules.Workers.Domain;

[JsonDerivedType(typeof(NonZeroExit), typeDiscriminator: "non_zero_exit")]
[JsonDerivedType(typeof(TimedOut), typeDiscriminator: "timed_out")]
[JsonDerivedType(typeof(ContainerError), typeDiscriminator: "container_error")]
public abstract record FailureReason
{
    private FailureReason()
    {
    }

    public sealed record NonZeroExit(int ExitCode) : FailureReason;

    public sealed record TimedOut : FailureReason;

    public sealed record ContainerError(string Message) : FailureReason;
}
