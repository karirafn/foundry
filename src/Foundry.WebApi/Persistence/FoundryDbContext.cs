using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Modules.Settings.Infrastructure.Configurations;
using Foundry.Modules.Workers.Infrastructure.Configurations;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Foundry.WebApi.Persistence;

public sealed class FoundryDbContext(
    DbContextOptions<FoundryDbContext> options,
    IDataProtectionProvider? dataProtectionProvider = null) : DbContext(options)
{
    private readonly IDataProtectionProvider _dataProtectionProvider =
        dataProtectionProvider ?? DataProtectionProvider.Create("Foundry");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IssueConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerRunConfiguration).Assembly);

        modelBuilder.ApplyConfiguration(new GlobalSettingsConfiguration(_dataProtectionProvider));
    }
}
