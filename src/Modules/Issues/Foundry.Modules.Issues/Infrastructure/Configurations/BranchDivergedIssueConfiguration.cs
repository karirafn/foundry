using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class BranchDivergedIssueConfiguration : IEntityTypeConfiguration<BranchDivergedIssue>
{
    private const int DivergenceReasonMaxLength = 30;

    private static readonly ValueConverter<BranchDivergenceReason, string> DivergenceReasonConverter = new(
        reason => BranchDivergenceReasonTokens.ToToken(reason),
        token => FromStored(token));

    private static BranchDivergenceReason FromStored(string token) =>
        BranchDivergenceReasonTokens.FromToken(token)
            ?? throw new InvalidOperationException($"Unknown stored divergence reason: {token}");

    public void Configure(EntityTypeBuilder<BranchDivergedIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("worker_run_id");

        builder.Property(i => i.BranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("branch_name");

        builder.Property(i => i.SideBranchName)
            .HasMaxLength(IssueColumnLimits.BranchNameMaxLength)
            .IsUnicode(false)
            .HasColumnName("side_branch_name");

        builder.Property(i => i.DivergenceReason)
            .HasConversion(DivergenceReasonConverter)
            .HasMaxLength(DivergenceReasonMaxLength)
            .IsUnicode(false)
            .HasColumnName("divergence_reason");

        builder.Property(i => i.PullRequestUrl)
            .HasMaxLength(IssueColumnLimits.PullRequestUrlMaxLength)
            .IsUnicode(false)
            .HasColumnName("pull_request_url");

        builder.Property(i => i.FromRevision)
            .HasColumnName("from_revision");
    }
}
