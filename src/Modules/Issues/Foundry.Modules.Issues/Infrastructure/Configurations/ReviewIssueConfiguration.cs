using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class ReviewIssueConfiguration : IEntityTypeConfiguration<ReviewIssue>
{
    private const int BranchNameMaxLength = 500;
    private const int PullRequestUrlMaxLength = 2000;

    public void Configure(EntityTypeBuilder<ReviewIssue> builder)
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

        builder.Property(i => i.FeedbackCutoffAt)
            .HasColumnName("feedback_cutoff_at");
    }
}
