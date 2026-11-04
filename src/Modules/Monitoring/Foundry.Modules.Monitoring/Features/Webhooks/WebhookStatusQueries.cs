using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Webhooks;

internal sealed class WebhookStatusQueries(DbContext db, WebhookIngress webhookIngress) : IWebhookStatusQueries
{
    public async Task<bool> IsWebhookActiveAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken)
    {
        WebhookStatus? status = await db.Set<MonitoredRepository>()
            .AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Select(r => (WebhookStatus?)r.WebhookStatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (status != WebhookStatus.Active)
        {
            return false;
        }

        WebhookIngressState ingress = await webhookIngress.GetStateAsync(cancellationToken);
        return ingress.IsConfigured;
    }
}
