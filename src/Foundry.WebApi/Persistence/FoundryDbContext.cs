using Foundry.Modules.Credentials.Infrastructure.Configurations;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Modules.Settings.Infrastructure.Configurations;
using Foundry.Modules.Workers.Infrastructure.Configurations;
using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using CredentialsInfrastructure = Foundry.Modules.Credentials.Infrastructure.Configurations;
using MonitoringInfrastructure = Foundry.Modules.Monitoring.Infrastructure.Configurations;

namespace Foundry.WebApi.Persistence;

public sealed class FoundryDbContext(
    DbContextOptions<FoundryDbContext> options,
    IDataProtectionProvider? dataProtectionProvider = null,
    ILoggerFactory? loggerFactory = null) : DbContext(options)
{
    private readonly IDataProtectionProvider _dataProtectionProvider =
        dataProtectionProvider ?? DataProtectionProvider.Create("Foundry");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IssueConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerRunConfiguration).Assembly);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());

        ILogger<MonitoringInfrastructure.EncryptedStringConverter>? monitoringConverterLogger =
            loggerFactory?.CreateLogger<MonitoringInfrastructure.EncryptedStringConverter>();

        ILogger<CredentialsInfrastructure.EncryptedStringConverter>? credentialsConverterLogger =
            loggerFactory?.CreateLogger<CredentialsInfrastructure.EncryptedStringConverter>();

        modelBuilder.ApplyConfiguration(new MonitoredRepositoryConfiguration());
        modelBuilder.ApplyConfiguration(
            new CredentialConfiguration(_dataProtectionProvider, monitoringConverterLogger));
        modelBuilder.ApplyConfiguration(new CredentialNamespaceConfiguration());

        modelBuilder.ApplyConfiguration(new GlobalSettingsConfiguration());

        modelBuilder.ApplyConfiguration(
            new ClaudeAccountConfiguration(_dataProtectionProvider, credentialsConverterLogger));
    }
}
