using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

public sealed class MonitoredRepositoryConfiguration : IEntityTypeConfiguration<MonitoredRepository>
{
    private const int SlugMaxLength = 500;
    private const int EligibilityStatusMaxLength = 20;

    private static RepositoryEligibility DeserializeEligibility(string json)
    {
        return JsonSerializer.Deserialize<RepositoryEligibility>(json, (JsonSerializerOptions?)null)
            ?? throw new InvalidOperationException($"Failed to deserialize RepositoryEligibility from JSON: {json}");
    }

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

        ValueConverter<RepositoryEligibility?, string?> eligibilityConverter = new(
            eligibility => eligibility == null
                ? null
                : JsonSerializer.Serialize(eligibility, (JsonSerializerOptions?)null),
            json => json == null
                ? null
                : DeserializeEligibility(json));

        builder.Property(r => r.Eligibility)
            .HasConversion(eligibilityConverter)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("eligibility");

        builder.Property(r => r.EligibilityStatus)
            .HasMaxLength(EligibilityStatusMaxLength)
            .IsUnicode(false)
            .HasColumnName("eligibility_status");

        builder.HasIndex(r => r.EligibilityStatus)
            .HasDatabaseName("ix_monitored_repositories_eligibility_status");

        builder.HasIndex(r => r.Slug)
            .IsUnique()
            .HasDatabaseName("ix_monitored_repositories_slug");

        builder.HasIndex(r => r.AccountId)
            .HasDatabaseName("ix_monitored_repositories_account_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
