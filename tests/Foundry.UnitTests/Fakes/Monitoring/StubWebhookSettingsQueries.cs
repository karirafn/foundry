using Foundry.Modules.Settings.Contracts.Queries;

namespace Foundry.UnitTests.Fakes.Monitoring;

/// <summary>Answers the webhook settings with a fixed public base URL and a 15-minute backstop.</summary>
internal sealed class StubWebhookSettingsQueries(string? publicBaseUrl) : IWebhookSettingsQueries
{
    public Task<WebhookSettingsSnapshot> GetWebhookSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new WebhookSettingsSnapshot(publicBaseUrl, TimeSpan.FromMinutes(15)));
}
