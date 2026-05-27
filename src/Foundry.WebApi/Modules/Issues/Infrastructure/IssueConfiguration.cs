using System.Text.Json;

using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.WebApi.Modules.Issues.Infrastructure;

public sealed class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    private const int TitleMaxLength = 500;
    private const int AuthorMaxLength = 200;
    private const int UrlMaxLength = 2000;

    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("issues");

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
                value => ((Result<IssueAuthor>.Success)IssueAuthor.Create(value)).Value)
            .HasMaxLength(AuthorMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("author");

        builder.Property(i => i.Url)
            .HasConversion(
                url => url.Value.ToString(),
                value => ((Result<ProviderUrl>.Success)ProviderUrl.Create(value)).Value)
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
            .HasColumnType("TEXT")
            .HasColumnName("labels");

        builder.Property(i => i.DetectedAt)
            .HasColumnName("detected_at");

        builder.HasDiscriminator<string>("state")
            .HasValue<DetectedIssue>("detected")
            .HasValue<QueuedIssue>("queued");

        builder.HasIndex(i => new { i.MonitoredRepositoryId, i.IssueNumber })
            .IsUnique()
            .HasDatabaseName("ix_issues_monitored_repository_id_issue_number");
    }
}
