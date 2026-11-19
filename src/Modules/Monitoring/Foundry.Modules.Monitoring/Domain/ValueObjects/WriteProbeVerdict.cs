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
    /// The token lacks a permission Foundry needs. <see cref="ProbedAt"/> stamps when the probe ran so
    /// the poller can re-probe after <see cref="Entities.MonitoredRepository.DeniedWriteProbeCooldown"/>:
    /// a permission gained in place on the provider leaves the token string unchanged, so no
    /// credential event would otherwise re-probe it. Legacy rows persisted without the field
    /// deserialize to <see langword="null"/>, which counts as due.
    /// </summary>
    internal sealed record Denied(DateTimeOffset? ProbedAt = null) : WriteProbeVerdict;

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
