using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Monitoring.Infrastructure.Configurations;

internal sealed class PostedCommentConfiguration : IEntityTypeConfiguration<PostedComment>
{
    public void Configure(EntityTypeBuilder<PostedComment> builder)
    {
        builder.ToTable("posted_comments");

        builder.HasKey(c => new { c.MonitoredRepositoryId, c.CommentId });

        builder.Property(c => c.MonitoredRepositoryId)
            .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>())
            .HasColumnName("monitored_repository_id");

        builder.Property(c => c.CommentId)
            .HasConversion(id => id.Value, value => FromStored(value))
            .HasColumnName("comment_id");

        builder.Property(c => c.PostedAt)
            .HasColumnName("posted_at");

        builder.HasOne<MonitoredRepository>()
            .WithMany()
            .HasForeignKey(c => c.MonitoredRepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    // Stored IDs were validated when they were recorded.
    private static ProviderCommentId FromStored(long value) =>
        ((Result<ProviderCommentId>.Success)ProviderCommentId.Create(value)).Value;
}
