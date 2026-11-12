using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class PublishFailedIssueConfiguration : IEntityTypeConfiguration<PublishFailedIssue>
{
    public void Configure(EntityTypeBuilder<PublishFailedIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("worker_run_id");

        builder.Property(i => i.BranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.PullRequestUrl)
            .HasMaxLength(IssueColumnLimits.PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");

        builder.Property(i => i.Trigger)
            .HasConversion((ValueConverter)RevisionTriggerJsonConversion.Converter, RevisionTriggerJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("revision_trigger");

        builder.Property(i => i.ChecksFixAttempts)
            .HasDefaultValue(0)
            .HasColumnName("checks_fix_attempts");

        builder.Property(i => i.RebaseConflictAttempts)
            .HasDefaultValue(0)
            .HasColumnName("rebase_conflict_attempts");

        builder.Ignore(i => i.IsRevision);

        builder.Property(i => i.FailureReason)
            .HasMaxLength(IssueColumnLimits.FailureReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_reason");

        builder.Property(i => i.FailedAt)
            .HasColumnName("failed_at");
    }
}
