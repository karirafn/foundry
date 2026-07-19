using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.IntegrationTests.Modules.Monitoring;

/// <summary>
/// Seeds eligibility state on monitored repositories directly via DbContext.
/// Use when the target state cannot be reached via the HTTP API (which does not
/// expose an eligibility-set endpoint; eligibility is derived by the evaluator).
/// </summary>
internal static class RepositoryEligibilitySeeder
{
    /// <summary>
    /// Sets the eligibility of the given repositories to <see cref="RepositoryEligibility.Eligible"/>.
    /// </summary>
    internal static async Task SetEligibleAsync(
        FoundryWebAppFactory factory,
        params Guid[] repositoryIds)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        HashSet<Guid> idSet = repositoryIds.ToHashSet();

        // Load all and filter client-side — EF cannot translate strongly-typed ID Contains in SQLite.
        List<MonitoredRepository> repos = (await dbContext.Set<MonitoredRepository>()
            .ToListAsync(CancellationToken.None))
            .Where(r => idSet.Contains(r.Id.Value))
            .ToList();

        foreach (MonitoredRepository repo in repos)
        {
            repo.SetEligibility(new RepositoryEligibility.Eligible());
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
