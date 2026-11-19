using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Settings.Features;

internal static class GlobalSettingsDbContextExtensions
{
    internal static Task<GlobalSettings?> FindGlobalSettingsAsync(this DbContext dbContext, CancellationToken cancellationToken) =>
        GlobalSettingsRow(dbContext).FirstOrDefaultAsync(cancellationToken);

    internal static Task<GlobalSettings?> FindGlobalSettingsNoTrackingAsync(this DbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.QueryGlobalSettingsNoTracking().FirstOrDefaultAsync(cancellationToken);

    internal static IQueryable<GlobalSettings> QueryGlobalSettingsNoTracking(this DbContext dbContext) =>
        GlobalSettingsRow(dbContext).AsNoTracking();

    private static IQueryable<GlobalSettings> GlobalSettingsRow(DbContext dbContext) =>
        dbContext.Set<GlobalSettings>().Where(s => s.Id == GlobalSettingsId.Default);
}
