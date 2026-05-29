using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class InProgressIssueConfiguration : IEntityTypeConfiguration<InProgressIssue>
{
    public void Configure(EntityTypeBuilder<InProgressIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasColumnName("worker_run_id");
    }
}
