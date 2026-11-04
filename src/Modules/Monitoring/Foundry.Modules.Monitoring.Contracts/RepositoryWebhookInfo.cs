namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Manual webhook registration details for one repository (ADR 0085). Never carries the secret —
/// that is returned only by the explicit reveal endpoint (<see cref="RepositoryWebhookSecret"/>).
/// </summary>
/// <param name="Status">The effective Webhook Status: <c>not-configured</c>, <c>not-registered</c>,
/// <c>pending</c>, <c>active</c> or <c>silent</c>.</param>
/// <param name="PayloadUrl">The payload URL to register; <see langword="null"/> when ingress is not configured.</param>
/// <param name="ContentType">The content type to select on the provider (GitHub only; GitLab always sends JSON).</param>
/// <param name="Events">The exact events (GitHub) or triggers (GitLab) to subscribe the webhook to.</param>
/// <param name="BackstopIntervalMinutes">The interval an <c>active</c> repository polls at.</param>
/// <param name="Provider">The provider the webhook is registered on: <c>github</c> or <c>gitlab</c>. Drives
/// the provider-specific registration instructions (GitLab: "Secret token" and the "Test" action).</param>
public sealed record RepositoryWebhookInfo(
    string Status,
    string? PayloadUrl,
    string ContentType,
    IReadOnlyList<string> Events,
    int BackstopIntervalMinutes,
    string Provider);

/// <summary>The revealed webhook secret, for manual registration on the provider.</summary>
public sealed record RepositoryWebhookSecret(string Secret);
