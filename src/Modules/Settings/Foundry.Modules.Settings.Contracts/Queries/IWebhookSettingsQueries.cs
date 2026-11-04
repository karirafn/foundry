namespace Foundry.Modules.Settings.Contracts.Queries;

/// <summary>
/// The webhook ingress settings (ADR 0085): the operator-supplied public base URL, or
/// <see langword="null"/> when unset, and the Backstop Interval that <c>Active</c> repositories poll at.
/// </summary>
public sealed record WebhookSettingsSnapshot(string? PublicBaseUrl, TimeSpan BackstopInterval);

public interface IWebhookSettingsQueries
{
    Task<WebhookSettingsSnapshot> GetWebhookSettingsAsync(CancellationToken cancellationToken);
}
