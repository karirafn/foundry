using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class RebasePendingIssueConfiguration : IEntityTypeConfiguration<RebasePendingIssue>
{
    public void Configure(EntityTypeBuilder<RebasePendingIssue> builder)
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

        builder.Property(i => i.EnteredRebasePendingAt)
            .HasColumnName("entered_rebase_pending_at");

        builder.Property(i => i.FeedbackCutoffAt)
            .HasColumnName("feedback_cutoff_at");

        builder.Property(i => i.RebaseConflictAttempts)
            .HasDefaultValue(0)
            .HasColumnName("rebase_conflict_attempts");

        builder.Property(i => i.Reason)
            .HasConversion(RebasePendingReasonJsonConversion.Converter, RebasePendingReasonJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("rebase_pending_reason");

        // Shares the merge-pending head column: a catch-up carries the head its refused merge was pinned to.
        builder.Property(i => i.CatchUpHeadSha)
            .HasMaxLength(IssueColumnLimits.HeadShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("merge_head_sha");

        // Shares the checks-pending column: the owed checks retry is carried from the rebase into the checks gate.
        builder.Property(i => i.OwedChecksRetryHeadSha)
            .HasMaxLength(IssueColumnLimits.HeadShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("owed_checks_retry_head_sha");

        // A Before Ready rebase holds the outcomes and head it hands to review in the review state's columns.
        builder.Property(i => i.ChecksOutcome)
            .HasConversion((ValueConverter)ChecksOutcomeJsonConversion.Converter, ChecksOutcomeJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("checks_outcome");

        builder.Property(i => i.ReviewOutcome)
            .HasConversion(ReviewOutcomeJsonConversion.Converter, ReviewOutcomeJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("review_outcome");

        builder.Property(i => i.VerifiedHeadSha)
            .HasMaxLength(IssueColumnLimits.HeadShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("verified_head_sha");

        builder.Property(i => i.AutoMerge)
            .HasDefaultValue(false)
            .HasColumnName("review_auto_merge");
    }
}
