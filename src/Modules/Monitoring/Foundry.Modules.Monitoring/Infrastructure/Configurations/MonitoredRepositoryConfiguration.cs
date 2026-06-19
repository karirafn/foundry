using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

public sealed class MonitoredRepositoryConfiguration : IEntityTypeConfiguration<MonitoredRepository>
{
    private const int SlugMaxLength = 500;
    private const int HostMaxLength = 253;

    public void Configure(EntityTypeBuilder<MonitoredRepository> builder)
    {
        builder.ToTable("monitored_repositories");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>())
            .HasColumnName("id");

        builder.Property(r => r.Slug)
            .HasConversion(
                slug => slug.ToString(),
                value => ((Result<RepositorySlug>.Success)RepositorySlug.Create(value)).Value)
            .HasMaxLength(SlugMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("slug");

        builder.Property(r => r.Host)
            .HasMaxLength(HostMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("host");

        builder.Property(r => r.AccountId)
            .HasConversion(new StronglyTypedIdValueConverter<AccountId>())
            .HasColumnName("account_id");

        builder.Property(r => r.PollInterval)
            .HasColumnName("poll_interval");

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active");

        builder.Property(r => r.LastPolledAt)
            .HasColumnName("last_polled_at");

        builder.HasIndex(r => new { r.Host, r.Slug })
            .IsUnique()
            .HasDatabaseName("ix_monitored_repositories_host_slug");

        builder.HasIndex(r => r.AccountId)
            .HasDatabaseName("ix_monitored_repositories_account_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
