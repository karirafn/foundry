using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class ContinuationQueuedIssueConfiguration : IEntityTypeConfiguration<ContinuationQueuedIssue>
{
    public void Configure(EntityTypeBuilder<ContinuationQueuedIssue> builder)
    {
        builder.Property(i => i.BranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.LatestProgress)
            .HasMaxLength(2000)
            .HasColumnType("TEXT")
            .HasColumnName("latest_progress");
    }
}
