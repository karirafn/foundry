using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

public sealed class AwaitingInputIssueConfiguration : IEntityTypeConfiguration<AwaitingInputIssue>
{
    public void Configure(EntityTypeBuilder<AwaitingInputIssue> builder)
    {
        builder.Property(i => i.WorkerRunId)
            .HasConversion(new StronglyTypedIdValueConverter<WorkerRunId>())
            .HasColumnName("worker_run_id");

        builder.Property(i => i.ResumeTarget)
            .HasConversion(AwaitingInputJsonConversion.ResumeTargetConverter, AwaitingInputJsonConversion.ResumeTargetComparer)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("resume_target");

        builder.Property(i => i.QuestionPost)
            .HasConversion(AwaitingInputJsonConversion.QuestionPostConverter, AwaitingInputJsonConversion.QuestionPostComparer)
            .HasMaxLength(int.MaxValue)
            .IsUnicode(true)
            .HasColumnType("TEXT")
            .HasColumnName("question_post");

        builder.Ignore(i => i.Questions);
    }
}
