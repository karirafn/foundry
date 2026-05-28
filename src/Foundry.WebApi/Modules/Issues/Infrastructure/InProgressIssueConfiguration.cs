using Foundry.WebApi.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.WebApi.Modules.Issues.Infrastructure;

public sealed class InProgressIssueConfiguration : IEntityTypeConfiguration<InProgressIssue>
{
    public void Configure(EntityTypeBuilder<InProgressIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasColumnName("worker_run_id");
    }
}
