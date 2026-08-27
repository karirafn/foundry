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

    /// <summary>
    /// The probe result is indeterminate — either a transient transport failure or a rate-limit
    /// exhaustion prevented a definitive answer. <see cref="Reason"/> distinguishes the two cases
    /// so the eligibility composer can surface the correct cause (Unreachable vs. rate-limited).
    /// Legacy rows persisted without a <c>Reason</c> field deserialize to
    /// <see cref="UnknownReason.Transport"/> (the safe default).
    /// </summary>
    internal sealed record Unknown(
        DateTimeOffset? LastAttemptedAt = null,
        UnknownReason Reason = UnknownReason.Transport) : WriteProbeVerdict;
}
