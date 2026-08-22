using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

[JsonDerivedType(typeof(Granted), typeDiscriminator: "granted")]
[JsonDerivedType(typeof(Denied), typeDiscriminator: "denied")]
[JsonDerivedType(typeof(Unknown), typeDiscriminator: "unknown")]
public abstract record WriteProbeVerdict
{
    private WriteProbeVerdict() { }

    public sealed record Granted : WriteProbeVerdict;

    public sealed record Denied : WriteProbeVerdict;

    public sealed record Unknown : WriteProbeVerdict;
}
