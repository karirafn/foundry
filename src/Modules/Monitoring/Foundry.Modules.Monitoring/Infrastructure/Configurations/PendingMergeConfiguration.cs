using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class PendingMergeConfiguration : IEntityTypeConfiguration<PendingMerge>
{
    public void Configure(EntityTypeBuilder<PendingMerge> builder)
    {
        builder.ToTable("pending_merges");

        builder.HasKey(m => new { m.MonitoredRepositoryId, m.IssueNumber });

        builder.Property(m => m.MonitoredRepositoryId)
            .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>())
            .HasColumnName("monitored_repository_id");

        builder.Property(m => m.IssueNumber)
            .HasColumnName("issue_number");

        builder.Property(m => m.EnteredMergePendingAt)
            .HasColumnName("entered_merge_pending_at");

        builder.Property(m => m.TransientFailures)
            .HasColumnName("transient_failures");

        builder.Property(m => m.NextAttemptAt)
            .HasColumnName("next_attempt_at");

        builder.Property(m => m.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Property(m => m.QueuedAt)
            .HasColumnName("queued_at");

        builder.Property(m => m.CatchUpRounds)
            .HasDefaultValue(0)
            .HasColumnName("catch_up_rounds");

        builder.Property(m => m.AwaitsCatchUp)
            .HasDefaultValue(false)
            .HasColumnName("awaits_catch_up");

        builder.Ignore(m => m.IsResolved);
        builder.Ignore(m => m.IsQueued);

        builder.HasOne<MonitoredRepository>()
            .WithMany()
            .HasForeignKey(m => m.MonitoredRepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
