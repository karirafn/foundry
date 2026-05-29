using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Infrastructure.Configurations;

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IssueConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerRunConfiguration).Assembly);
    }
}
