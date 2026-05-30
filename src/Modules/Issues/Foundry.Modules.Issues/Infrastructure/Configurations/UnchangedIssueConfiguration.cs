using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class UnchangedIssueConfiguration : IEntityTypeConfiguration<UnchangedIssue>
{
    public void Configure(EntityTypeBuilder<UnchangedIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasColumnName("worker_run_id");
    }
}
