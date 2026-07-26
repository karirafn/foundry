using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");

        builder.HasKey(e => new { e.EventId, e.Handler });

        builder.Property(e => e.EventId)
            .HasColumnType("TEXT")
            .HasColumnName("event_id");

        builder.Property(e => e.Handler)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("handler");

        builder.Property(e => e.ProcessedAt)
            .IsRequired()
            .HasColumnName("processed_at");
    }
}
