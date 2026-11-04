using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal sealed class WebhookSettingsQueries(DbContext dbContext) : IWebhookSettingsQueries
{
    public async Task<WebhookSettingsSnapshot> GetWebhookSettingsAsync(CancellationToken cancellationToken)
    {
        WebhookSettingsSnapshot? snapshot = await dbContext.Set<GlobalSettings>()
            .AsNoTracking()
            .Select(s => new WebhookSettingsSnapshot(
                s.WebhookPublicBaseUrl,
                TimeSpan.FromMinutes(s.BackstopIntervalMinutes)))
            .FirstOrDefaultAsync(cancellationToken);

        return snapshot ?? new WebhookSettingsSnapshot(
            null,
            TimeSpan.FromMinutes(GlobalSettings.DefaultBackstopIntervalMinutes));
    }
}
