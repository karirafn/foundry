using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class RevisionFailedIssueConfiguration : IEntityTypeConfiguration<RevisionFailedIssue>
{
    private const int BranchNameMaxLength = 500;
    private const int PullRequestUrlMaxLength = 2000;
    private const int FailureReasonMaxLength = 500;

    public void Configure(EntityTypeBuilder<RevisionFailedIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasColumnName("worker_run_id");

        builder.Property(i => i.BranchName)
            .HasMaxLength(BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.PullRequestUrl)
            .HasMaxLength(PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");

        builder.Property(i => i.FailureReason)
            .HasMaxLength(FailureReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_reason");

        builder.Property(i => i.FailedAt)
            .HasColumnName("failed_at");

        builder.Property(i => i.ReviewComments)
            .HasConversion(ReviewCommentsJsonConversion.Converter, ReviewCommentsJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("review_comments");
    }
}
