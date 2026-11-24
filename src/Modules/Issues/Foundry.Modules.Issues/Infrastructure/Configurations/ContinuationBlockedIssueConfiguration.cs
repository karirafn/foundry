using Foundry.Modules.Issues.Domain.Entities.States;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class ContinuationBlockedIssueConfiguration : IEntityTypeConfiguration<ContinuationBlockedIssue>
{
    public void Configure(EntityTypeBuilder<ContinuationBlockedIssue> builder)
    {
        builder.Property(i => i.BranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.FailureReason)
            .HasMaxLength(ContinuationQueuedIssue.FailureReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_reason");

        builder.Property(i => i.ContinuedWithoutAnswer)
            .HasColumnName("continued_without_answer")
            .HasDefaultValue(false);
    }
}
