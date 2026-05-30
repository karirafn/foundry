using Foundry.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class FailedIssueConfiguration : IEntityTypeConfiguration<FailedIssue>
{
    private const int FailureReasonMaxLength = 500;

    public void Configure(EntityTypeBuilder<FailedIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasColumnName("worker_run_id");

        builder.Property(i => i.FailureReason)
            .HasMaxLength(FailureReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_reason");

        builder.Property(i => i.FailedAt)
            .HasColumnName("failed_at");
    }
}
