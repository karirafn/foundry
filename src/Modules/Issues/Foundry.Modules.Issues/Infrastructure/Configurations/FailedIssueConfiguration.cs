using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class FailedIssueConfiguration : IEntityTypeConfiguration<FailedIssue>
{
    private const int FailureReasonMaxLength = 500;
    private const int FailureCategoryMaxLength = 100;

    public void Configure(EntityTypeBuilder<FailedIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("worker_run_id");

        builder.Property(i => i.FailureReason)
            .HasMaxLength(FailureReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_reason");

        builder.Property(i => i.FailureCategory)
            .HasConversion(FailureCategoryValueConverter.Converter)
            .HasMaxLength(FailureCategoryMaxLength)
            .IsUnicode(false)
            .HasColumnName("failure_category");

        builder.Property(i => i.FailedAt)
            .HasColumnName("failed_at");
    }
}
