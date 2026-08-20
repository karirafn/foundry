using Foundry.Modules.Issues.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

// Registers the abstract intermediate in the TPH hierarchy so EF can translate
// OfType<ClaimableIssue>() and pattern-match queries to
// WHERE state IN ('queued','revision_queued','continuation_queued').
// No HasValue<ClaimableIssue>() is added — abstract intermediates carry no discriminator row.
internal sealed class ClaimableIssueConfiguration : IEntityTypeConfiguration<ClaimableIssue>
{
    public void Configure(EntityTypeBuilder<ClaimableIssue> builder)
    {
        builder.HasBaseType<Issue>();
    }
}
