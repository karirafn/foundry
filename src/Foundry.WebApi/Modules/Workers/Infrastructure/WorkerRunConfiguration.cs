using System.Text.Json;

using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.WebApi.Modules.Workers.Infrastructure;

public sealed class WorkerRunConfiguration : IEntityTypeConfiguration<WorkerRun>
{
    private const int DiscriminatorMaxLength = 20;

    public void Configure(EntityTypeBuilder<WorkerRun> builder)
    {
        builder.ToTable("worker_runs");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("id");

        builder.Property(r => r.IssueId)
            .HasConversion(new StronglyTypedIdValueConverter<IssueId>())
            .HasColumnName("issue_id");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at");

        builder.HasDiscriminator<string>("state")
            .HasValue<StartingRun>("starting")
            .HasValue<ActiveRun>("active")
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

    public void Configure(EntityTypeBuilder<ActiveRun> builder)
    {
        builder.Property(r => r.ContainerId)
            .HasMaxLength(ContainerIdMaxLength)
            .IsUnicode(false)
            .IsRequired()
            .HasColumnName("container_id");

        builder.Property(r => r.StartedAt)
            .HasColumnName("started_at");

        builder.Property(r => r.LatestProgress)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnName("latest_progress");
    }
}

public sealed class CompletedRunConfiguration : IEntityTypeConfiguration<CompletedRun>
{
    private const int BranchNameMaxLength = 500;
    private const int PullRequestUrlMaxLength = 2000;

    public void Configure(EntityTypeBuilder<CompletedRun> builder)
    {
        builder.Property(r => r.ExitCode)
            .HasColumnName("exit_code");

        builder.Property(r => r.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(r => r.BranchName)
            .HasMaxLength(BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(r => r.PullRequestUrl)
            .HasMaxLength(PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");
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
    }
}
