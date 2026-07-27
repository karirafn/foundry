using Foundry.Modules.Issues.Domain.Entities.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class ContinuableFailedIssueConfiguration : IEntityTypeConfiguration<ContinuableFailedIssue>
{
    private const int FailureReasonMaxLength = 500;
    private const int FailureCategoryMaxLength = 100;

    public void Configure(EntityTypeBuilder<ContinuableFailedIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
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
            .HasMaxLength(FailureReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_reason");

        builder.Property(i => i.FailureCategory)
            .HasMaxLength(FailureCategoryMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_category");

        builder.Property(i => i.FailedAt)
            .HasColumnName("failed_at");
    }
}
