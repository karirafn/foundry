using System.Globalization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class IssueStepTransitionConfiguration : IEntityTypeConfiguration<IssueStepTransition>
{
    private const int StateMaxLength = 30;
    private const int LifecycleStepMaxLength = 30;
    private const int CauseMaxLength = 100;

    private static IssueLifecycleStep ConvertToLifecycleStep(string token) =>
        IssueLifecycleStepTokens.FromToken(token)
            ?? throw new InvalidOperationException($"Stored lifecycle step '{token}' is not a known step.");

    public void Configure(EntityTypeBuilder<IssueStepTransition> builder)
    {
        builder.ToTable("issue_step_transitions");

        builder.HasKey(t => t.Sequence);

        builder.Property(t => t.Sequence)
            .ValueGeneratedOnAdd()
            .HasColumnName("sequence");

        // No foreign key to the issues table: every transition deletes the issue row and adds the next state under the
        // same id, so a cascading key would wipe the log on each transition (ADR 0122).
        builder.Property(t => t.IssueId)
            .HasConversion(new StronglyTypedIdValueConverter<IssueId>())
            .IsRequired()
            .HasColumnName("issue_id");

        builder.Property(t => t.State)
            .IsRequired()
            .HasMaxLength(StateMaxLength)
            .HasColumnName("state");

        builder.Property(t => t.LifecycleStep)
            .HasConversion(step => IssueLifecycleStepTokens.ToToken(step), token => ConvertToLifecycleStep(token))
            .IsRequired()
            .HasMaxLength(LifecycleStepMaxLength)
            .HasColumnName("lifecycle_step");

        builder.Property(t => t.Round)
            .IsRequired()
            .HasColumnName("round");

        builder.Property(t => t.EnteredAt)
            .HasConversion(
                dto => dto.UtcDateTime.ToString("O"),
                s => DateTimeOffset.Parse(s, null, DateTimeStyles.RoundtripKind))
            .HasColumnType("TEXT")
            .IsRequired()
            .HasColumnName("entered_at");

        builder.Property(t => t.Cause)
            .HasMaxLength(CauseMaxLength)
            .HasColumnName("cause");

        builder.HasIndex(t => t.IssueId)
            .HasDatabaseName("ix_issue_step_transitions_issue_id");
    }
}
