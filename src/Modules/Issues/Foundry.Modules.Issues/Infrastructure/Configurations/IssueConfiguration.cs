using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    private const int TitleMaxLength = 500;
    private const int AuthorMaxLength = 200;
    private const int UrlMaxLength = 2000;
    private const int DiscriminatorMaxLength = 20;

    private static IssueAuthor ConvertToIssueAuthor(string value) =>
        IssueAuthor.Create(value) is Result<IssueAuthor>.Success s
            ? s.Value
            : throw new InvalidOperationException($"Stored author value '{value}' failed validation.");

    private static ProviderUrl ConvertToProviderUrl(string value) =>
        ProviderUrl.Create(value) is Result<ProviderUrl>.Success s
            ? s.Value
            : throw new InvalidOperationException($"Stored URL value '{value}' failed validation.");

    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("issues", t =>
        {
            t.HasCheckConstraint(
                "ck_issues_in_progress_worker_run_id",
                "state <> 'in_progress' OR worker_run_id IS NOT NULL");
            t.HasCheckConstraint(
                "ck_issues_review_fields",
                "state <> 'review' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_unchanged_worker_run_id",
                "state <> 'unchanged' OR worker_run_id IS NOT NULL");
            t.HasCheckConstraint(
                "ck_issues_failed_fields",
                "state <> 'failed' OR (worker_run_id IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_completed_completed_at",
                "state <> 'completed' OR completed_at IS NOT NULL");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasConversion(new StronglyTypedIdValueConverter<IssueId>())
            .HasColumnName("id");

        builder.Property(i => i.MonitoredRepositoryId)
            .HasConversion(new StronglyTypedIdValueConverter<MonitoredRepositoryId>())
            .HasColumnName("monitored_repository_id");

        builder.Property(i => i.IssueNumber)
            .HasColumnName("issue_number");

        builder.Property(i => i.Title)
            .HasMaxLength(TitleMaxLength)
            .IsUnicode(true)
            .IsRequired()
            .HasColumnName("title");

        builder.Property(i => i.Body)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .IsRequired()
            .HasColumnName("body");

        builder.Property(i => i.Author)
            .HasConversion(
                author => author.Value,
                value => ConvertToIssueAuthor(value))
            .HasMaxLength(AuthorMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("author");

        builder.Property(i => i.Url)
            .HasConversion(
                url => url.Value.ToString(),
                value => ConvertToProviderUrl(value))
            .HasMaxLength(UrlMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("url");

        ValueConverter<IReadOnlyList<string>, string> labelsConverter = new(
            labels => JsonSerializer.Serialize(labels, (JsonSerializerOptions?)null),
            json => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null)
                ?? new List<string>()));

        ValueComparer<IReadOnlyList<string>> labelsComparer = new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            labels => labels.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode(StringComparison.Ordinal))),
            labels => (IReadOnlyList<string>)labels.ToList());

        builder.Property(i => i.Labels)
            .HasConversion(labelsConverter, labelsComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("labels");

        ValueConverter<IReadOnlyList<int>, string> blockedByConverter = new(
            blockedBy => JsonSerializer.Serialize(blockedBy, (JsonSerializerOptions?)null),
            json => (IReadOnlyList<int>)(JsonSerializer.Deserialize<List<int>>(json, (JsonSerializerOptions?)null)
                ?? new List<int>()));

        ValueComparer<IReadOnlyList<int>> blockedByComparer = new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            blockedBy => blockedBy.Aggregate(0, (hash, n) => HashCode.Combine(hash, n.GetHashCode())),
            blockedBy => (IReadOnlyList<int>)blockedBy.ToList());

        builder.Property(i => i.BlockedBy)
            .HasConversion(blockedByConverter, blockedByComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasDefaultValueSql("'[]'")
            .HasColumnName("blocked_by");

        builder.Property(i => i.DetectedAt)
            .HasColumnName("detected_at");

        builder.HasDiscriminator<string>("state")
            .HasValue<DetectedIssue>("detected")
            .HasValue<QueuedIssue>("queued")
            .HasValue<BlockedIssue>("blocked")
            .HasValue<InProgressIssue>("in_progress")
            .HasValue<ReviewIssue>("review")
            .HasValue<UnchangedIssue>("unchanged")
            .HasValue<FailedIssue>("failed")
            .HasValue<CompletedIssue>("completed")
            .IsComplete(true);

        builder.Property<string>("state")
            .HasMaxLength(DiscriminatorMaxLength)
            .HasColumnName("state");

        builder.HasIndex(i => new { i.MonitoredRepositoryId, i.IssueNumber })
            .IsUnique()
            .HasDatabaseName("ix_issues_monitored_repository_id_issue_number");
    }
}
