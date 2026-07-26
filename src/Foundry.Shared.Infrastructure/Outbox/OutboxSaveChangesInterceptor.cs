using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class OutboxSaveChangesInterceptor(IServiceProvider services) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is DbContext context)
        {
            IntegrationEventCollector collector = services.GetRequiredService<IntegrationEventCollector>();
            collector.DrainInto(context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is DbContext context)
        {
            IntegrationEventCollector collector = services.GetRequiredService<IntegrationEventCollector>();
            collector.DrainInto(context);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
