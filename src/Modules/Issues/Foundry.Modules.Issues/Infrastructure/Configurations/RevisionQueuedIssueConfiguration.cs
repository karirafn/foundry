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

        builder.Property(i => i.Trigger)
            .HasConversion(RevisionTriggerJsonConversion.Converter, RevisionTriggerJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("revision_trigger");

        builder.Property(i => i.ChecksFixAttempts)
            .HasDefaultValue(0)
            .HasColumnName("checks_fix_attempts");
    }
}
