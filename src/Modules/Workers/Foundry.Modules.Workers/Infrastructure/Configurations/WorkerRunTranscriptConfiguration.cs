using System.Globalization;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

public sealed class WorkerRunTranscriptConfiguration : IEntityTypeConfiguration<WorkerRunTranscript>
{
    public void Configure(EntityTypeBuilder<WorkerRunTranscript> builder)
    {
        builder.ToTable("worker_run_transcripts");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("worker_run_id");

        builder.Property(t => t.Transcript)
            .IsRequired()
            .HasColumnName("transcript");

        // Stored as ISO 8601 TEXT so the retention sweep's cutoff comparison translates to SQL.
        builder.Property(t => t.CapturedAt)
            .IsRequired()
            .HasConversion(
                dto => dto.UtcDateTime.ToString("O"),
                s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .HasColumnType("TEXT")
            .HasColumnName("captured_at");

        builder.HasIndex(t => t.CapturedAt);

        builder.HasOne<WorkerRun>()
            .WithOne()
            .HasForeignKey<WorkerRunTranscript>(t => t.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
