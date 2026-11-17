using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class IssueWatchConfiguration : IEntityTypeConfiguration<IssueWatch>
{
    private const int WaitMaxLength = 32;

    // EF Core never passes null to a converter, so both directions see a value.
    private static readonly ValueConverter<IssueBodyHash?, string?> BodyHashConverter = new(
        hash => hash!.Value,
        stored => IssueBodyHash.FromStored(stored!));

    public void Configure(EntityTypeBuilder<IssueWatch> builder)
    {
        builder.ToTable("issue_watches");

        builder.HasKey(w => new { w.MonitoredRepositoryId, w.IssueNumber });

        builder.Property(w => w.MonitoredRepositoryId)
            .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>())
            .HasColumnName("monitored_repository_id");

        builder.Property(w => w.IssueNumber)
            .HasColumnName("issue_number");

        builder.Property(w => w.Wait)
            .HasConversion<string>()
            .HasMaxLength(WaitMaxLength)
            .IsUnicode(false)
            .HasColumnName("wait");

        builder.Property(w => w.BaselineBodyHash)
            .HasConversion(BodyHashConverter)
            .HasColumnName("baseline_body_hash");

        builder.Property<IssueBodyHash?>("PendingBodyHash")
            .HasConversion(BodyHashConverter)
            .HasColumnName("pending_body_hash");

        builder.Property<DateTimeOffset?>("PendingBodySeenAt")
            .HasColumnName("pending_body_first_seen_at");

        builder.Ignore(w => w.PendingBodyChange);

        builder.HasOne<MonitoredRepository>()
            .WithMany()
            .HasForeignKey(w => w.MonitoredRepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
