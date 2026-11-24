using Foundry.Modules.Issues.Domain.Entities.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class RevisionBlockedIssueConfiguration : IEntityTypeConfiguration<RevisionBlockedIssue>
{
    public void Configure(EntityTypeBuilder<RevisionBlockedIssue> builder)
    {
        builder.Property(i => i.BranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.PullRequestUrl)
            .HasMaxLength(IssueColumnLimits.PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");

        builder.Property(i => i.Trigger)
            .HasConversion(RevisionTriggerJsonConversion.Converter, RevisionTriggerJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("revision_trigger");

        builder.Property(i => i.PriorFeedbackCutoffAt)
            .HasColumnName("prior_feedback_cutoff_at");

        builder.Property(i => i.RebaseConflictAttempts)
            .HasDefaultValue(0)
            .HasColumnName("rebase_conflict_attempts");

        builder.Property(i => i.ContinuedWithoutAnswer)
            .HasColumnName("continued_without_answer")
            .HasDefaultValue(false);
    }
}
