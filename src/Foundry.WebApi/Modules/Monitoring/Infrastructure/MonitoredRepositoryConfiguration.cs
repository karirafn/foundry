using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.WebApi.Modules.Monitoring.Infrastructure;

public sealed class MonitoredRepositoryConfiguration : IEntityTypeConfiguration<MonitoredRepository>
{
    private const int SlugMaxLength = 500;

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

        builder.Property(r => r.AccountId)
            .HasConversion(new StronglyTypedIdValueConverter<AccountId>())
            .HasColumnName("account_id");

        builder.Property(r => r.PollInterval)
            .HasColumnName("poll_interval");

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active");

        builder.Property(r => r.LastPolledAt)
            .HasColumnName("last_polled_at");

        builder.HasIndex(r => r.Slug)
            .IsUnique()
            .HasDatabaseName("ix_monitored_repositories_slug");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
