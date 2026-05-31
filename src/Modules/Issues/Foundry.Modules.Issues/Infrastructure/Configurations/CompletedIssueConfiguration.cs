using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class CompletedIssueConfiguration : IEntityTypeConfiguration<CompletedIssue>
{
    public void Configure(EntityTypeBuilder<CompletedIssue> builder)
    {
        builder.Property(i => i.BranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("branch_name");

        builder.Property(i => i.PullRequestUrl)
            .HasMaxLength(IssueColumnLimits.PullRequestUrlMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("pull_request_url");

        builder.Property(i => i.CompletedAt)
            .HasColumnName("completed_at");
    }
}
