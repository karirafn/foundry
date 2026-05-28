using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.WebApi.Modules.Workers.Infrastructure;

public sealed class WorkerReportConfiguration : IEntityTypeConfiguration<WorkerReport>
{
    private const int ReportTypeMaxLength = 100;

    public void Configure(EntityTypeBuilder<WorkerReport> builder)
    {
        builder.ToTable("worker_reports");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerReportId>())
            .HasColumnName("id");

        builder.Property(r => r.WorkerRunId)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("worker_run_id");

        builder.Property(r => r.SequenceNumber)
            .HasColumnName("sequence_number");

        builder.Property(r => r.ReportType)
            .HasMaxLength(ReportTypeMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("report_type");

        builder.Property(r => r.Content)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .IsRequired()
            .HasColumnName("content");

        builder.Property(r => r.IngestedAt)
            .HasColumnName("ingested_at");

        builder.HasOne<WorkerRun>()
            .WithMany()
            .HasForeignKey(r => r.WorkerRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.WorkerRunId, r.SequenceNumber })
            .HasDatabaseName("ix_worker_reports_worker_run_id_sequence_number");
    }
}
