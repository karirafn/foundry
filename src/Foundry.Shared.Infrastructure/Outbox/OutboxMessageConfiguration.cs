using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnType("TEXT")
            .HasColumnName("id");

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("type");

        builder.Property(m => m.Payload)
            .IsRequired()
            .HasColumnType("TEXT")
            .HasColumnName("payload");

        builder.Property(m => m.OccurredAt)
            .IsRequired()
            .HasColumnName("occurred_at");

        builder.Property(m => m.Attempts)
            .IsRequired()
            .HasColumnName("attempts");

        builder.Property(m => m.ProcessedAt)
            .HasColumnName("processed_at");

        builder.Property(m => m.Error)
            .HasColumnType("TEXT")
            .HasColumnName("error");

        builder.HasIndex(m => new { m.ProcessedAt, m.OccurredAt })
            .HasDatabaseName("ix_outbox_messages_processed_at_occurred_at");
    }
}
