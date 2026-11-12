using System.Globalization;
using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
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
    private const int DiscriminatorMaxLength = 30;
    private const int IssueKindMaxLength = 50;

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
                "state <> 'review' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND feedback_cutoff_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_unchanged_worker_run_id",
                "state <> 'unchanged' OR worker_run_id IS NOT NULL");
            t.HasCheckConstraint(
                "ck_issues_failed_fields",
                "state <> 'failed' OR (worker_run_id IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_completed_completed_at",
                "state <> 'completed' OR completed_at IS NOT NULL");
            t.HasCheckConstraint(
                "ck_issues_revision_queued_fields",
                "state <> 'revision_queued' OR (branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND revision_trigger IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_revision_in_progress_fields",
                "state <> 'revision_in_progress' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND revision_trigger IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_revision_failed_fields",
                "state <> 'revision_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND revision_trigger IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_continuable_failed_fields",
                "state <> 'continuable_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_continuation_queued_branch_name",
                "state <> 'continuation_queued' OR branch_name IS NOT NULL");
            t.HasCheckConstraint(
                "ck_issues_checks_pending_fields",
                "state <> 'checks_pending' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND entered_checks_pending_at IS NOT NULL AND feedback_cutoff_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_rebase_pending_fields",
                "state <> 'rebase_pending' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND entered_rebase_pending_at IS NOT NULL AND feedback_cutoff_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_awaiting_input_fields",
                "state <> 'awaiting_input' OR (worker_run_id IS NOT NULL AND resume_target IS NOT NULL AND question_post IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_triage_in_progress_worker_run_id",
                "state <> 'triage_in_progress' OR worker_run_id IS NOT NULL");
            t.HasCheckConstraint(
                "ck_issues_triage_failed_fields",
                "state <> 'triage_failed' OR (worker_run_id IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_publish_failed_fields",
                "state <> 'publish_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_publish_blocked_fields",
                "state <> 'publish_blocked' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND publish_blocked_at IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_issues_branch_diverged_fields",
                "state <> 'branch_diverged' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND side_branch_name IS NOT NULL)");
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

        builder.Property(i => i.Clarifications)
            .HasConversion(AwaitingInputJsonConversion.ClarificationsConverter, AwaitingInputJsonConversion.ClarificationsComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasDefaultValueSql("'[]'")
            .HasColumnName("clarifications");

        builder.Ignore(i => i.OpenClarification);

        builder.Property(i => i.TriageVerdict)
            .HasConversion((ValueConverter)TriageVerdictJsonConversion.Converter, TriageVerdictJsonConversion.Comparer)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .IsRequired(false)
            .HasColumnName("triage_verdict");

        builder.Ignore(i => i.ConflictingTriageKind);

        builder.Property(i => i.DetectedAt)
            .HasConversion(
                dto => dto.UtcDateTime.ToString("O"),
                s => DateTimeOffset.Parse(s, null, DateTimeStyles.RoundtripKind))
            .HasColumnType("TEXT")
            .HasColumnName("detected_at");

        ValueConverter<IssueKind, string> issueKindConverter = new(
            kind => kind.Value,
            value => ParseStoredIssueKind(value));

        // KindClassification is derived: its label half is stored as the labelled kind (a null column is no label),
        // its triage half is the triage verdict's kind.
        builder.Ignore(i => i.KindClassification);
        builder.Property<IssueKind?>("_labelledKind")
            .HasConversion((ValueConverter)issueKindConverter)
            .HasMaxLength(IssueKindMaxLength)
            .IsUnicode(false)
            .IsRequired(false)
            .HasColumnName("issue_kind");

        builder.HasDiscriminator<string>("state")
            .HasValue<DetectedIssue>("detected")
            .HasValue<FreshQueuedIssue>("queued")
            .HasValue<BlockedIssue>("blocked")
            .HasValue<TriageBlockedIssue>("triage_blocked")
            .HasValue<InProgressIssue>("in_progress")
            .HasValue<ReviewIssue>("review")
            .HasValue<UnchangedIssue>("unchanged")
            .HasValue<FailedIssue>("failed")
            .HasValue<ContinuableFailedIssue>("continuable_failed")
            .HasValue<ContinuationQueuedIssue>("continuation_queued")
            .HasValue<CompletedIssue>("completed")
            .HasValue<RevisionQueuedIssue>("revision_queued")
            .HasValue<RevisionInProgressIssue>("revision_in_progress")
            .HasValue<RevisionFailedIssue>("revision_failed")
            .HasValue<ChecksPendingIssue>("checks_pending")
            .HasValue<RebasePendingIssue>("rebase_pending")
            .HasValue<AwaitingInputIssue>("awaiting_input")
            .HasValue<TriageQueuedIssue>("triage_queued")
            .HasValue<TriageInProgressIssue>("triage_in_progress")
            .HasValue<TriageFailedIssue>("triage_failed")
            .HasValue<PublishFailedIssue>("publish_failed")
            .HasValue<PublishBlockedIssue>("publish_blocked")
            .HasValue<BranchDivergedIssue>("branch_diverged")
            .IsComplete(true);

        builder.Property<string>("state")
            .HasMaxLength(DiscriminatorMaxLength)
            .HasColumnName("state");

        builder.HasIndex(i => new { i.MonitoredRepositoryId, i.IssueNumber })
            .IsUnique()
            .HasDatabaseName("ix_issues_monitored_repository_id_issue_number");
    }

    private static IssueKind ParseStoredIssueKind(string value) =>
        IssueKind.FromLabel(value)
            ?? throw new InvalidOperationException($"Stored issue kind '{value}' is not a known issue kind.");
}
