using Foundry.Modules.Issues.Domain.Entities.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class RevisionQueuedIssueConfiguration : IEntityTypeConfiguration<RevisionQueuedIssue>
{
    public void Configure(EntityTypeBuilder<RevisionQueuedIssue> builder)
    {
        builder.Property(i => i.BranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.PullRequestUrl)
            .HasMaxLength(IssueColumnLimits.PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");

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
