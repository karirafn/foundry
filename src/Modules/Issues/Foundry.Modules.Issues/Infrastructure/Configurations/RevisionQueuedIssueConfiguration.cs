using System.Text.Json;

using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class RevisionQueuedIssueConfiguration : IEntityTypeConfiguration<RevisionQueuedIssue>
{
    private const int BranchNameMaxLength = 500;
    private const int PullRequestUrlMaxLength = 2000;

    public void Configure(EntityTypeBuilder<RevisionQueuedIssue> builder)
    {
        builder.Property(i => i.BranchName)
            .HasMaxLength(BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.PullRequestUrl)
            .HasMaxLength(PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");

        ValueConverter<IReadOnlyList<ReviewComment>, string> reviewCommentsConverter = new(
            comments => JsonSerializer.Serialize(comments, (JsonSerializerOptions?)null),
            json => (IReadOnlyList<ReviewComment>)(JsonSerializer.Deserialize<List<ReviewComment>>(
                json,
                (JsonSerializerOptions?)null)
                ?? new List<ReviewComment>()));

        ValueComparer<IReadOnlyList<ReviewComment>> reviewCommentsComparer = new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            comments => comments.Aggregate(
                0,
                (hash, c) => HashCode.Combine(hash, c.GetHashCode())),
            comments => (IReadOnlyList<ReviewComment>)comments.ToList());

        builder.Property(i => i.ReviewComments)
            .HasConversion(reviewCommentsConverter, reviewCommentsComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("review_comments");
    }
}
