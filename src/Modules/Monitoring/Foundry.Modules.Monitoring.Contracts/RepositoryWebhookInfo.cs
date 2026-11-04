namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Manual webhook registration details for one repository (ADR 0085). Never carries the secret —
/// that is returned only by the explicit reveal endpoint (<see cref="RepositoryWebhookSecret"/>).
/// </summary>
/// <param name="Status">The effective Webhook Status: <c>not-configured</c>, <c>not-registered</c>,
/// <c>pending</c>, <c>active</c> or <c>silent</c>.</param>
/// <param name="PayloadUrl">The payload URL to register; <see langword="null"/> when ingress is not configured.</param>
/// <param name="ContentType">The content type to select on the provider.</param>
/// <param name="Events">The exact events to subscribe the webhook to.</param>
/// <param name="BackstopIntervalMinutes">The interval an <c>active</c> repository polls at.</param>
public sealed record RepositoryWebhookInfo(
    string Status,
    string? PayloadUrl,
    string ContentType,
    IReadOnlyList<string> Events,
    int BackstopIntervalMinutes);

/// <summary>The revealed webhook secret, for manual registration on the provider.</summary>
public sealed record RepositoryWebhookSecret(string Secret);
