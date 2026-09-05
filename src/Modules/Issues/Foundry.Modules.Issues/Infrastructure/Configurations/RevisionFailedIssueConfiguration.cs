using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class RevisionFailedIssueConfiguration : IEntityTypeConfiguration<RevisionFailedIssue>
{
    public void Configure(EntityTypeBuilder<RevisionFailedIssue> builder)
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

        builder.Property(i => i.FailureReason)
            .HasMaxLength(IssueColumnLimits.FailureReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_reason");

        builder.Property(i => i.FailureCategory)
            .HasConversion(FailureCategoryValueConverter.Converter)
            .HasMaxLength(IssueColumnLimits.FailureCategoryMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_category");

        builder.Property(i => i.FailedAt)
            .HasColumnName("failed_at");

        builder.Property(i => i.ReviewComments)
            .HasConversion(ReviewCommentsJsonConversion.Converter, ReviewCommentsJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("review_comments");

        builder.Property(i => i.OmittedCommentCount)
            .HasDefaultValue(0)
            .HasColumnName("omitted_comment_count");

        builder.Property(i => i.NewestConsumedCommentAt)
            .HasColumnName("newest_consumed_comment_at");
    }
}
