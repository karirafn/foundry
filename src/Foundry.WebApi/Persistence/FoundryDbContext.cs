using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Modules.Workers.Infrastructure.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Foundry.WebApi.Persistence;

public sealed class FoundryDbContext(DbContextOptions<FoundryDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IssueConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerRunConfiguration).Assembly);
    }
}
