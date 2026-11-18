using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal sealed class WebhookSettingsQueries(DbContext dbContext) : IWebhookSettingsQueries
{
    public async Task<WebhookSettingsSnapshot> GetWebhookSettingsAsync(CancellationToken cancellationToken)
    {
        StoredWebhookSettings? stored = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => new StoredWebhookSettings(s.WebhookPublicBaseUrl, s.BackstopIntervalMinutes))
            .FirstOrDefaultAsync(cancellationToken);

        return stored is null
            ? new WebhookSettingsSnapshot(null, TimeSpan.FromMinutes(GlobalSettings.DefaultBackstopIntervalMinutes))
            : new WebhookSettingsSnapshot(stored.PublicBaseUrl?.Value, TimeSpan.FromMinutes(stored.BackstopIntervalMinutes));
    }

    private sealed record StoredWebhookSettings(WebhookPublicBaseUrl? PublicBaseUrl, int BackstopIntervalMinutes);
}
