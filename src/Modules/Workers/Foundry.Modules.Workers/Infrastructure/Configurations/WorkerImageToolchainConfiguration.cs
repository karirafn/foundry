using System.Globalization;

using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

public sealed class WorkerImageToolchainConfiguration : IEntityTypeConfiguration<WorkerImageToolchain>
{
    // A stored list that no longer reads as a toolchain (edited by hand) counts as unrecorded rather than failing the
    // query; the row's image then gets no toolchain-dependent section.
    private static readonly ValueConverter<Toolchain, string> EntriesConverter = new(
        toolchain => ToolchainJson.Serialize(toolchain),
        json => ToolchainJson.TryDeserialize(json) ?? Toolchain.Unrecorded);

    private static readonly ValueComparer<Toolchain> EntriesComparer = new(
        (left, right) => ReferenceEquals(left, right)
            || (left != null && right != null && left.Entries.SequenceEqual(right.Entries)),
        toolchain => toolchain.Entries.Aggregate(0, (hash, entry) => HashCode.Combine(hash, entry)),
        toolchain => toolchain);

    public void Configure(EntityTypeBuilder<WorkerImageToolchain> builder)
    {
        builder.ToTable("worker_image_toolchains");

        builder.HasKey(t => t.ImageId);

        builder.Property(t => t.ImageId)
            .HasMaxLength(WorkerImageToolchain.MaxImageIdLength)
            .IsUnicode(false)
            .HasColumnName("image_id");

        builder.Property(t => t.Toolchain)
            .HasConversion(EntriesConverter, EntriesComparer)
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
