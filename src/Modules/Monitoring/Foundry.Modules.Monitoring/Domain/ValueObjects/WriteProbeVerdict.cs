using System.Text.Json.Serialization;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

[JsonDerivedType(typeof(Granted), typeDiscriminator: "granted")]
[JsonDerivedType(typeof(Denied), typeDiscriminator: "denied")]
[JsonDerivedType(typeof(Unknown), typeDiscriminator: "unknown")]
internal abstract record WriteProbeVerdict
{
    private WriteProbeVerdict() { }

    internal sealed record Granted : WriteProbeVerdict;

    /// <summary>
    /// The token cannot write to the repository. <see cref="ProbedAt"/> is when the probe ran, so the
    /// poller can re-probe after the Denied cooldown. Legacy rows persisted without a <c>ProbedAt</c>
    /// field deserialize to <see langword="null"/> and are due at once.
    /// </summary>
    internal sealed record Denied(DateTimeOffset? ProbedAt) : WriteProbeVerdict;

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
