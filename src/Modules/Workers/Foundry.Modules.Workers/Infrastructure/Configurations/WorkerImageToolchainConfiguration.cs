using System.Globalization;

using Foundry.Modules.Workers.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

public sealed class WorkerImageToolchainConfiguration : IEntityTypeConfiguration<WorkerImageToolchain>
{
    public void Configure(EntityTypeBuilder<WorkerImageToolchain> builder)
    {
        builder.ToTable("worker_image_toolchains");

        builder.HasKey(t => t.ImageId);

        builder.Property(t => t.ImageId)
            .HasMaxLength(WorkerImageToolchain.MaxImageIdLength)
            .IsUnicode(false)
            .HasColumnName("image_id");

        builder.Property(t => t.Toolchain)
            .HasConversion(ToolchainValueConverters.Converter, ToolchainValueConverters.Comparer)
            .IsRequired()
            .HasColumnType("TEXT")
            .HasColumnName("entries");

        builder.Property(t => t.RecordedAt)
            .IsRequired()
            .HasConversion(
                dto => dto.UtcDateTime.ToString("O"),
                s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .HasColumnType("TEXT")
            .HasColumnName("recorded_at");
    }
}
