using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class ReviewIssueConfiguration : IEntityTypeConfiguration<ReviewIssue>
{
    public void Configure(EntityTypeBuilder<ReviewIssue> builder)
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

        builder.Property(i => i.FeedbackCutoffAt)
            .HasColumnName("feedback_cutoff_at");

        builder.Property(i => i.ChecksOutcome)
            .HasConversion(ChecksOutcomeJsonConversion.Converter, ChecksOutcomeJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("checks_outcome");

        builder.Property(i => i.RebaseOutcome)
            .HasConversion(RebaseOutcomeJsonConversion.Converter, RebaseOutcomeJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("rebase_outcome");

        builder.Property(i => i.RebaseConflictAttempts)
            .HasDefaultValue(0)
            .HasColumnName("rebase_conflict_attempts");

        builder.Property(i => i.VerifiedHeadSha)
            .HasMaxLength(IssueColumnLimits.HeadShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("verified_head_sha");
    }
}
