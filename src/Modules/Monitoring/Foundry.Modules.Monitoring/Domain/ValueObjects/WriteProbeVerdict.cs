using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

[JsonDerivedType(typeof(Granted), typeDiscriminator: "granted")]
[JsonDerivedType(typeof(Denied), typeDiscriminator: "denied")]
[JsonDerivedType(typeof(Unknown), typeDiscriminator: "unknown")]
internal abstract record WriteProbeVerdict
{
    private WriteProbeVerdict() { }

    internal sealed record Granted : WriteProbeVerdict;

    internal sealed record Denied : WriteProbeVerdict;

    internal sealed record Unknown(DateTimeOffset? LastAttemptedAt = null) : WriteProbeVerdict;
}
