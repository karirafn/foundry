using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

file static class WorkerRunValueConverters
{
    internal const int BranchNameMaxLength = 500;

    // Shared by ActiveRun, CompletedRun and FailedRun: the request is carried into the terminal state.
    internal const string CancelRequestedAtColumn = "cancel_requested_at";

    internal static readonly ValueConverter<BranchName, string> BranchNameConverter = new(
        bn => bn.Value,
        value => BranchName.From(value));

    internal static readonly ValueConverter<CommitSha, string> CommitShaConverter = new(
        sha => sha.Value,
        value => CommitSha.From(value));

    internal static readonly ValueConverter<MonitoredRepositoryId, Guid> MonitoredRepositoryIdConverter = new(
        id => id.Value,
        value => MonitoredRepositoryId.From(value));

    internal static readonly ValueConverter<IReadOnlyList<int>?, string> SuppliedOpenIssuesConverter = new(
        numbers => JsonSerializer.Serialize(numbers, JsonSerializerOptions.Default),
        json => DeserializeSuppliedOpenIssues(json));

    internal static readonly ValueComparer<IReadOnlyList<int>?> SuppliedOpenIssuesComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        numbers => numbers == null ? 0 : numbers.Aggregate(0, (hash, number) => HashCode.Combine(hash, number)),
        numbers => numbers == null ? null : numbers.ToList());

    private static List<int> DeserializeSuppliedOpenIssues(string json) =>
        JsonSerializer.Deserialize<List<int>>(json, JsonSerializerOptions.Default)
            ?? throw new InvalidOperationException("Failed to deserialize the supplied open issues.");
}

file static class RunVerdictValueConverters
{
    private static readonly JsonSerializerOptions QuestionsJsonOptions = new(JsonSerializerDefaults.Web);

    // Stored as the verdict schema's own tokens so the column reads the same as the run's structured_output.
    internal static readonly ValueConverter<RunVerdictStatus, string> StatusConverter = new(
        status => ToStored(status),
        value => FromStored(value));

    internal static readonly ValueConverter<IReadOnlyList<RunVerdictQuestion>?, string> QuestionsConverter = new(
        questions => JsonSerializer.Serialize(questions, QuestionsJsonOptions),
        json => DeserializeQuestions(json));

    internal static readonly ValueComparer<IReadOnlyList<RunVerdictQuestion>?> QuestionsComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        questions => questions == null ? 0 : questions.Aggregate(0, (hash, question) => HashCode.Combine(hash, question)),
        questions => questions == null ? null : questions.ToList());

    private static string ToStored(RunVerdictStatus status) => status switch
    {
        RunVerdictStatus.Done => "done",
        RunVerdictStatus.NeedsInput => "needs_input",
        RunVerdictStatus.Missing => "missing",
        RunVerdictStatus.Invalid => "invalid",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown verdict status."),
    };

    private static RunVerdictStatus FromStored(string value) => value switch
    {
        "done" => RunVerdictStatus.Done,
        "needs_input" => RunVerdictStatus.NeedsInput,
        "missing" => RunVerdictStatus.Missing,
        "invalid" => RunVerdictStatus.Invalid,
        _ => throw new InvalidOperationException($"Unknown stored verdict status: {value}"),
    };

    private static List<RunVerdictQuestion> DeserializeQuestions(string json) =>
        JsonSerializer.Deserialize<List<RunVerdictQuestion>>(json, QuestionsJsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize the verdict questions.");
}

public sealed class WorkerRunConfiguration : IEntityTypeConfiguration<WorkerRun>
{
    private const int DiscriminatorMaxLength = 20;
    private const int RoleMaxLength = 50;
    private const int ImageMaxLength = 500;
    private const int VerdictStatusMaxLength = 20;
    private const int CommitShaMaxLength = 64;

    public void Configure(EntityTypeBuilder<WorkerRun> builder)
    {
        builder.ToTable("worker_runs", t =>
        {
            t.HasCheckConstraint(
                "ck_worker_runs_active_container_id",
                "state <> 'active' OR container_id IS NOT NULL");

            t.HasCheckConstraint(
                "ck_worker_runs_active_started_at",
                "state <> 'active' OR started_at IS NOT NULL");

            t.HasCheckConstraint(
                "ck_worker_runs_completed_exit_code",
                "state <> 'completed' OR exit_code IS NOT NULL");

            t.HasCheckConstraint(
                "ck_worker_runs_completed_completed_at",
                "state <> 'completed' OR completed_at IS NOT NULL");

            t.HasCheckConstraint(
                "ck_worker_runs_failed_reason",
                "state <> 'failed' OR reason IS NOT NULL");

            t.HasCheckConstraint(
                "ck_worker_runs_failed_failed_at",
                "state <> 'failed' OR failed_at IS NOT NULL");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("id");

        builder.Property(r => r.IssueId)
            .HasConversion(new StronglyTypedIdValueConverter<IssueId>())
            .HasColumnName("issue_id");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(r => r.Role)
            .HasConversion(new ValueConverter<WorkerRole, string>(role => role.Name, name => WorkerRole.FromStored(name)))
            .HasMaxLength(RoleMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasDefaultValue(WorkerRole.Implement)
            .HasColumnName("role");

        builder.Property(r => r.Image)
            .HasMaxLength(ImageMaxLength)
            .IsUnicode(false)
            .HasColumnName("image");

        builder.Property(r => r.VerdictStatus)
            .HasConversion(RunVerdictValueConverters.StatusConverter)
            .HasMaxLength(VerdictStatusMaxLength)
            .IsUnicode(false)
            .HasColumnName("verdict_status");

        builder.Property(r => r.VerdictQuestions)
            .HasConversion(RunVerdictValueConverters.QuestionsConverter, RunVerdictValueConverters.QuestionsComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("verdict_questions");

        // The stored list is bounded by what Foundry supplies at dispatch: at most the open-issue index cap
        // (TrackerContextReader.MaxOpenIssues) plus the referenced-issue cap
        // (ReferencedIssueSelection.MaxReferencedIssues), never anything the model returns.
        builder.Property(r => r.SuppliedOpenIssueNumbers)
            .HasConversion(
                WorkerRunValueConverters.SuppliedOpenIssuesConverter,
                WorkerRunValueConverters.SuppliedOpenIssuesComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("supplied_open_issue_numbers");

        builder.Ignore(r => r.Verdict);

        builder.Property(r => r.BaseDefaultBranchHead)
            .HasConversion(WorkerRunValueConverters.CommitShaConverter)
            .HasMaxLength(CommitShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("base_default_branch_head");

        builder.Property(r => r.BaseRunBranchHead)
            .HasConversion(WorkerRunValueConverters.CommitShaConverter)
            .HasMaxLength(CommitShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("base_run_branch_head");

        builder.Ignore(r => r.Base);

        builder.Property(r => r.PublishedHead)
            .HasConversion(WorkerRunValueConverters.CommitShaConverter)
            .HasMaxLength(CommitShaMaxLength)
            .IsUnicode(false)
            .HasColumnName("published_head");

        builder.Property(r => r.PublishedBranchCommitCount)
            .HasColumnName("published_branch_commit_count");

        builder.Property(r => r.DivergedSideBranch)
            .HasConversion(WorkerRunValueConverters.BranchNameConverter)
            .HasMaxLength(WorkerRunValueConverters.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("diverged_side_branch");

        builder.Property(r => r.RevertedFiles)
            .HasConversion(DiffGateValueConverters.RevertedFilesConverter, DiffGateValueConverters.RevertedFilesComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .IsRequired()
            .HasDefaultValue(Array.Empty<RevertedFile>())
            .HasColumnName("reverted_files");

        builder.HasDiscriminator<string>("state")
            .HasValue<StartingRun>("starting")
            .HasValue<ActiveRun>("active")
            .HasValue<PublishingRun>("publishing")
            .HasValue<PublishFailedRun>("publish_failed")
            .HasValue<PublishBlockedRun>("publish_blocked")
            .HasValue<CompletedRun>("completed")
            .HasValue<FailedRun>("failed")
            .IsComplete(true);

        builder.Property<string>("state")
            .HasMaxLength(DiscriminatorMaxLength)
            .HasColumnName("state");

        builder.HasIndex(r => r.IssueId)
            .HasDatabaseName("ix_worker_runs_issue_id");
    }
}

public sealed class ActiveRunConfiguration : IEntityTypeConfiguration<ActiveRun>
{
    private const int ContainerIdMaxLength = 200;

    private static readonly ValueConverter<ContainerId, string> ContainerIdConverter = new(
        id => id.Value,
        value => ContainerId.From(value));

    public void Configure(EntityTypeBuilder<ActiveRun> builder)
    {
        builder.Property(r => r.ContainerId)
            .HasConversion(ContainerIdConverter)
            .HasMaxLength(ContainerIdMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("container_id");

        builder.Property(r => r.StartedAt)
            .HasColumnName("started_at");

        builder.Property(r => r.BranchName)
            .HasConversion(WorkerRunValueConverters.BranchNameConverter)
            .HasMaxLength(WorkerRunValueConverters.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(r => r.MonitoredRepositoryId)
            .HasConversion(WorkerRunValueConverters.MonitoredRepositoryIdConverter)
            .HasColumnName("monitored_repository_id");

        builder.Property(r => r.LastActivityAt)
            .HasColumnName("last_activity_at");

        builder.Property(r => r.CancelRequestedAt)
            .HasColumnName(WorkerRunValueConverters.CancelRequestedAtColumn);
    }
}

public sealed class CompletedRunConfiguration : IEntityTypeConfiguration<CompletedRun>
{
    private const int PullRequestUrlMaxLength = 2000;

    private static readonly ValueConverter<PullRequestUrl, string> PullRequestUrlConverter = new(
        url => url.Value,
        value => PullRequestUrl.From(value));

    public void Configure(EntityTypeBuilder<CompletedRun> builder)
    {
        builder.Property(r => r.ExitCode)
            .HasColumnName("exit_code");

        builder.Property(r => r.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(r => r.BranchName)
            .HasConversion(WorkerRunValueConverters.BranchNameConverter)
            .HasMaxLength(WorkerRunValueConverters.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(r => r.PullRequestUrl)
            .HasConversion(PullRequestUrlConverter)
            .HasMaxLength(PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");

        builder.Property(r => r.ContainerOutput)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("container_output");

        builder.Property(r => r.CancelRequestedAt)
            .HasColumnName(WorkerRunValueConverters.CancelRequestedAtColumn);

        builder.OwnsOne(r => r.ResultSummary, summary =>
        {
            summary.Property(s => s.ResultText)
                .HasMaxLength(RunResultSummary.ResultTextMaxLength)
                .IsUnicode(false)
                .HasColumnName("result_text");

            summary.Property(s => s.Subtype)
                .HasMaxLength(RunResultSummary.SubtypeMaxLength)
                .IsUnicode(false)
                .HasColumnName("subtype");

            summary.Property(s => s.IsError)
                .HasColumnName("is_error");

            summary.Property(s => s.DurationMs)
                .HasColumnName("duration_ms");

            summary.Property(s => s.NumTurns)
                .HasColumnName("num_turns");

            summary.Property(s => s.TotalCostUsd)
                .HasPrecision(18, 6)
                .HasColumnName("total_cost_usd");

            summary.Property(s => s.InputTokens)
                .HasColumnName("input_tokens");

            summary.Property(s => s.CacheReadInputTokens)
                .HasColumnName("cache_read_input_tokens");

            summary.Property(s => s.CacheCreationInputTokens)
                .HasColumnName("cache_creation_input_tokens");

            summary.Property(s => s.OutputTokens)
                .HasColumnName("output_tokens");

            summary.Ignore(s => s.Tokens);
        });
    }
}

public sealed class FailedRunConfiguration : IEntityTypeConfiguration<FailedRun>
{
    private static FailureReason DeserializeReason(string json)
    {
        return JsonSerializer.Deserialize<FailureReason>(json, (JsonSerializerOptions?)null)
            ?? throw new InvalidOperationException($"Failed to deserialize FailureReason from JSON: {json}");
    }

    public void Configure(EntityTypeBuilder<FailedRun> builder)
    {
        ValueConverter<FailureReason, string> reasonConverter = new(
            reason => JsonSerializer.Serialize(reason, (JsonSerializerOptions?)null),
            json => DeserializeReason(json));

        builder.Property(r => r.Reason)
            .HasConversion(reasonConverter)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("reason");

        builder.Property(r => r.FailedAt)
            .HasColumnName("failed_at");

        builder.Property(r => r.BranchName)
            .HasConversion(WorkerRunValueConverters.BranchNameConverter)
            .HasMaxLength(WorkerRunValueConverters.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(r => r.ContainerOutput)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("container_output");

        builder.Property(r => r.CancelRequestedAt)
            .HasColumnName(WorkerRunValueConverters.CancelRequestedAtColumn);

        builder.OwnsOne(r => r.ResultSummary, summary =>
        {
            summary.Property(s => s.ResultText)
                .HasMaxLength(RunResultSummary.ResultTextMaxLength)
                .IsUnicode(false)
                .HasColumnName("result_text");

            summary.Property(s => s.Subtype)
                .HasMaxLength(RunResultSummary.SubtypeMaxLength)
                .IsUnicode(false)
                .HasColumnName("subtype");

            summary.Property(s => s.IsError)
                .HasColumnName("is_error");

            summary.Property(s => s.DurationMs)
                .HasColumnName("duration_ms");

            summary.Property(s => s.NumTurns)
                .HasColumnName("num_turns");

            summary.Property(s => s.TotalCostUsd)
                .HasPrecision(18, 6)
                .HasColumnName("total_cost_usd");

            summary.Property(s => s.InputTokens)
                .HasColumnName("input_tokens");

            summary.Property(s => s.CacheReadInputTokens)
                .HasColumnName("cache_read_input_tokens");

            summary.Property(s => s.CacheCreationInputTokens)
                .HasColumnName("cache_creation_input_tokens");

            summary.Property(s => s.OutputTokens)
                .HasColumnName("output_tokens");

            summary.Ignore(s => s.Tokens);
        });
    }
}

public sealed class UnpublishedRunConfiguration : IEntityTypeConfiguration<UnpublishedRun>
{
    private const int StopReasonMaxLength = 20;
    private const int PullRequestSummaryMaxLength = 1000;

    private static readonly JsonSerializerOptions SubjectsJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueConverter<RunStopReason, string> StopReasonConverter = new(
        reason => ToStored(reason),
        value => FromStored(value));

    private static readonly ValueConverter<IReadOnlyList<string>, string> SubjectsConverter = new(
        subjects => JsonSerializer.Serialize(subjects, SubjectsJsonOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, SubjectsJsonOptions) ?? new List<string>());

    private static readonly ValueComparer<IReadOnlyList<string>> SubjectsComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        subjects => subjects.Aggregate(0, (hash, subject) => HashCode.Combine(hash, subject)),
        subjects => subjects.ToList());

    public void Configure(EntityTypeBuilder<UnpublishedRun> builder)
    {
        builder.Property(r => r.StartedAt)
            .HasColumnName("started_at");

        builder.Property(r => r.BranchName)
            .HasConversion(WorkerRunValueConverters.BranchNameConverter)
            .HasMaxLength(WorkerRunValueConverters.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(r => r.MonitoredRepositoryId)
            .HasConversion(WorkerRunValueConverters.MonitoredRepositoryIdConverter)
            .HasColumnName("monitored_repository_id");

        builder.Property(r => r.CancelRequestedAt)
            .HasColumnName(WorkerRunValueConverters.CancelRequestedAtColumn);

        builder.Property(r => r.ExitCode)
            .HasColumnName("exit_status_code");

        builder.Property(r => r.OomKilled)
            .HasColumnName("exit_oom_killed");

        builder.Property(r => r.StopReason)
            .HasConversion(StopReasonConverter)
            .HasMaxLength(StopReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("stop_reason");

        builder.Property(r => r.ExitedAt)
            .HasColumnName("exited_at");

        builder.Property(r => r.ContainerOutput)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("container_output");

        builder.Property(r => r.PullRequestSummary)
            .HasMaxLength(PullRequestSummaryMaxLength)
            .HasColumnName("pull_request_summary");

        builder.Property(r => r.PullRequestBody)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("pull_request_body");

        builder.Property(r => r.FailedPublishAttempts)
            .HasColumnName("failed_publish_attempts");

        builder.Property(r => r.PublishedCommitSubjects)
            .HasConversion(SubjectsConverter, SubjectsComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("published_commit_subjects");

        builder.Property(r => r.PublishedAt)
            .HasColumnName("published_at");

        builder.Property(r => r.PullRequestSettledAt)
            .HasColumnName("pull_request_settled_at");

        builder.Property(r => r.PendingTranscript)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("pending_transcript");

        builder.Property(r => r.PendingTranscriptCapturedAt)
            .HasColumnName("pending_transcript_captured_at");

        builder.Ignore(r => r.Exit);
        builder.Ignore(r => r.DiscardsAt);
        builder.Ignore(r => r.PullRequestText);
    }

    private static string ToStored(RunStopReason reason) => reason switch
    {
        RunStopReason.Exited => "exited",
        RunStopReason.TimedOut => "timed_out",
        RunStopReason.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown stop reason."),
    };

    private static RunStopReason FromStored(string value) => value switch
    {
        "exited" => RunStopReason.Exited,
        "timed_out" => RunStopReason.TimedOut,
        "cancelled" => RunStopReason.Cancelled,
        _ => throw new InvalidOperationException($"Unknown stored stop reason: {value}"),
    };
}

public sealed class PublishingRunConfiguration : IEntityTypeConfiguration<PublishingRun>
{
    public void Configure(EntityTypeBuilder<PublishingRun> builder)
    {
        builder.Ignore(r => r.HasPublished);
    }
}

public sealed class PublishFailedRunConfiguration : IEntityTypeConfiguration<PublishFailedRun>
{
    public void Configure(EntityTypeBuilder<PublishFailedRun> builder)
    {
        builder.Property(r => r.PublishFailureReason)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("publish_failure_reason");

        builder.Property(r => r.PublishFailedAt)
            .HasColumnName("publish_failed_at");

        builder.Property(r => r.NextPublishAttemptAt)
            .HasColumnName("next_publish_attempt_at");

        builder.Ignore(r => r.IsExhausted);
    }
}

public sealed class PublishBlockedRunConfiguration : IEntityTypeConfiguration<PublishBlockedRun>
{
    public void Configure(EntityTypeBuilder<PublishBlockedRun> builder)
    {
        builder.Property(r => r.SecretFindings)
            .HasConversion(DiffGateValueConverters.SecretFindingsConverter, DiffGateValueConverters.SecretFindingsComparer)
            .HasMaxLength(int.MaxValue)
            .HasColumnType("TEXT")
            .HasColumnName("secret_findings");

        builder.Property(r => r.PublishBlockedAt)
            .HasColumnName("publish_blocked_at");
    }
}

// The Diff gate's facts are stored as JSON through explicit row shapes, so a domain rename never changes stored data and
// a finding row has no field for a matched value.
file static class DiffGateValueConverters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static readonly ValueConverter<IReadOnlyList<RevertedFile>, string> RevertedFilesConverter = new(
        files => JsonSerializer.Serialize(files.Select(ToRow).ToList(), JsonOptions),
        json => FromRows(JsonSerializer.Deserialize<List<RevertedFileRow>>(json, JsonOptions)));

    internal static readonly ValueComparer<IReadOnlyList<RevertedFile>> RevertedFilesComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        files => files.Aggregate(0, (hash, file) => HashCode.Combine(hash, file)),
        files => files.ToList());

    internal static readonly ValueConverter<IReadOnlyList<SecretFinding>, string> SecretFindingsConverter = new(
        findings => JsonSerializer.Serialize(findings.Select(ToRow).ToList(), JsonOptions),
        json => FromRows(JsonSerializer.Deserialize<List<SecretFindingRow>>(json, JsonOptions)));

    internal static readonly ValueComparer<IReadOnlyList<SecretFinding>> SecretFindingsComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        findings => findings.Aggregate(0, (hash, finding) => HashCode.Combine(hash, finding)),
        findings => findings.ToList());

    private static RevertedFileRow ToRow(RevertedFile file) => new(
        file.Path,
        file.Reason switch
        {
            RevertedFileReason.Oversized => "oversized",
            RevertedFileReason.Binary => "binary",
            _ => throw new ArgumentOutOfRangeException(nameof(file), file.Reason, "Unknown reverted-file reason."),
        },
        file.SizeBytes);

    private static List<RevertedFile> FromRows(List<RevertedFileRow>? rows) =>
        rows?.Select(row => new RevertedFile(
            row.Path,
            row.Reason switch
            {
                "oversized" => RevertedFileReason.Oversized,
                "binary" => RevertedFileReason.Binary,
                _ => throw new InvalidOperationException($"Unknown stored reverted-file reason: {row.Reason}"),
            },
            row.SizeBytes)).ToList() ?? [];

    private static SecretFindingRow ToRow(SecretFinding finding) =>
        new(finding.Path, finding.Line, finding.Commit.Value, finding.RuleId);

    private static List<SecretFinding> FromRows(List<SecretFindingRow>? rows) =>
        rows?.Select(row => new SecretFinding(row.Path, row.Line, CommitSha.From(row.Commit), row.RuleId)).ToList() ?? [];

    private sealed record RevertedFileRow(string Path, string Reason, long SizeBytes);

    private sealed record SecretFindingRow(string Path, int Line, string Commit, string RuleId);
}
