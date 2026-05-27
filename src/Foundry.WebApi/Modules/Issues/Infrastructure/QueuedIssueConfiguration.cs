using Foundry.WebApi.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.WebApi.Modules.Issues.Infrastructure;

public sealed class QueuedIssueConfiguration : IEntityTypeConfiguration<QueuedIssue>
{
    public void Configure(EntityTypeBuilder<QueuedIssue> builder)
    {
        // All QueuedIssue configuration is inherited from IssueConfiguration.
    }
}
