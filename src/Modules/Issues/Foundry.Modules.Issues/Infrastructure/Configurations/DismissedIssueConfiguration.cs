using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class DismissedIssueConfiguration : IEntityTypeConfiguration<DismissedIssue>
{
    public void Configure(EntityTypeBuilder<DismissedIssue> builder)
    {
        builder.Property(i => i.CompletedAt)
            .HasColumnName("completed_at");
    }
}
