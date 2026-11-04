namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The per-repository health of its provider webhook (ADR 0085). Only <see cref="Active"/> polls at
/// the Backstop Interval; every other status polls at the normal interval.
/// </summary>
public enum WebhookStatus
{
    /// <summary>Webhook ingress is not configured (no webhook port or no public base URL). Derived, never persisted.</summary>
    NotConfigured,

    /// <summary>The operator has not recorded that the webhook was added on the provider.</summary>
    NotRegistered,

    /// <summary>The operator recorded the webhook; no verified delivery has arrived yet.</summary>
    Pending,

    /// <summary>A verified delivery has arrived and no missed change has been found since.</summary>
    Active,

    /// <summary>A scheduled poll found a change no delivery announced; polling is back at the normal interval.</summary>
    Silent,
}
