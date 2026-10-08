using Foundry.Modules.Credentials.Infrastructure.Configurations;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Modules.Settings.Infrastructure.Configurations;
using Foundry.Modules.Workers.Infrastructure.Configurations;
using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

using CredentialsInfrastructure = Foundry.Modules.Credentials.Infrastructure.Configurations;
using DataProtectionProviderFactory = Microsoft.AspNetCore.DataProtection.DataProtectionProvider;
using MonitoringInfrastructure = Foundry.Modules.Monitoring.Infrastructure.Configurations;

namespace Foundry.WebApi.Persistence;

public sealed class FoundryDbContext(
    DbContextOptions<FoundryDbContext> options,
    IDataProtectionProvider? dataProtectionProvider = null,
    ILoggerFactory? loggerFactory = null) : DbContext(options)
{
    // Shared default provider: created once per process when no provider is injected.
    // Using a static ensures all contexts without an explicit provider share the same key ring,
    // so a model cached under one context can be reused by another default context.
    private static readonly IDataProtectionProvider SharedDefaultProvider =
        DataProtectionProviderFactory.Create("Foundry");

    private readonly IDataProtectionProvider _dataProtectionProvider =
        dataProtectionProvider ?? SharedDefaultProvider;

    // Exposed as internal so FoundryDbContextModelCacheKeyFactory can read them for cache-key computation.
    internal IDataProtectionProvider DataProtectionProvider => _dataProtectionProvider;

    internal ILoggerFactory? ContextLoggerFactory => loggerFactory;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, FoundryDbContextModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IssueConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerRunConfiguration).Assembly);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());

        ILogger<MonitoringInfrastructure.EncryptedStringConverter>? monitoringConverterLogger =
            loggerFactory?.CreateLogger<MonitoringInfrastructure.EncryptedStringConverter>();

        ILogger<CredentialsInfrastructure.ApiKeyCredentialConverter>? credentialsConverterLogger =
            loggerFactory?.CreateLogger<CredentialsInfrastructure.ApiKeyCredentialConverter>();

        modelBuilder.ApplyConfiguration(new MonitoredRepositoryConfiguration());
        modelBuilder.ApplyConfiguration(
            new CredentialConfiguration(_dataProtectionProvider, monitoringConverterLogger));
        modelBuilder.ApplyConfiguration(new CredentialNamespaceConfiguration());

        modelBuilder.ApplyConfiguration(new GlobalSettingsConfiguration());

        modelBuilder.ApplyConfiguration(
            new ClaudeAccountConfiguration(_dataProtectionProvider, credentialsConverterLogger));
    }
}
