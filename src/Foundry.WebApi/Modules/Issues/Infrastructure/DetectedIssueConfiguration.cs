using Foundry.WebApi.Modules.Issues.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.WebApi.Modules.Issues.Infrastructure;

public sealed class DetectedIssueConfiguration : IEntityTypeConfiguration<DetectedIssue>
{
    public void Configure(EntityTypeBuilder<DetectedIssue> builder)
    {
        // All DetectedIssue configuration is inherited from IssueConfiguration.
    }
}
