using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Workers.Domain;

using Microsoft.EntityFrameworkCore;

namespace Foundry.WebApi.Persistence;

public sealed class FoundryDbContext(DbContextOptions<FoundryDbContext> options) : DbContext(options)
{
    public DbSet<MonitoredRepository> MonitoredRepositories => Set<MonitoredRepository>();

    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<WorkerRun> WorkerRuns => Set<WorkerRun>();

    public DbSet<WorkerReport> WorkerReports => Set<WorkerReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FoundryDbContext).Assembly);
    }
}
