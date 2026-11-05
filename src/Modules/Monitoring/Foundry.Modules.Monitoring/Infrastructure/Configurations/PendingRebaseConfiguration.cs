using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class PendingRebaseConfiguration : IEntityTypeConfiguration<PendingRebase>
{
    private const int ShaMaxLength = 64;

    public void Configure(EntityTypeBuilder<PendingRebase> builder)
    {
        builder.ToTable("pending_rebases");

        builder.HasKey(r => new { r.MonitoredRepositoryId, r.IssueNumber });

        builder.Property(r => r.MonitoredRepositoryId)
            .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>())
            .HasColumnName("monitored_repository_id");

        builder.Property(r => r.IssueNumber)
            .HasColumnName("issue_number");

        builder.Property(r => r.EnteredRebasePendingAt)
            .HasColumnName("entered_rebase_pending_at");

        builder.Property(r => r.PreRebaseHeadSha)
            .HasMaxLength(ShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("pre_rebase_head_sha");

        builder.Property(r => r.RequestedAt)
            .HasColumnName("requested_at");

        builder.Property(r => r.TransientFailures)
            .HasColumnName("transient_failures");

        builder.Property(r => r.NextAttemptAt)
            .HasColumnName("next_attempt_at");

        builder.Property(r => r.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Ignore(r => r.IsResolved);
        builder.Ignore(r => r.IsAwaitingLanding);

        builder.HasOne<MonitoredRepository>()
            .WithMany()
            .HasForeignKey(r => r.MonitoredRepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
