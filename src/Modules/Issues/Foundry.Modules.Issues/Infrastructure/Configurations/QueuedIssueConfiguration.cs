using Foundry.Modules.Issues.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

// Registers the abstract intermediate in the TPH hierarchy so EF can translate
// OfType<QueuedIssue>() and pattern-match queries to
// WHERE state IN ('queued','revision_queued','continuation_queued').
// No HasValue<QueuedIssue>() is added — abstract intermediates carry no discriminator row.
internal sealed class QueuedIssueConfiguration : IEntityTypeConfiguration<QueuedIssue>
{
    public void Configure(EntityTypeBuilder<QueuedIssue> builder)
    {
        builder.HasBaseType<Issue>();
    }
}
