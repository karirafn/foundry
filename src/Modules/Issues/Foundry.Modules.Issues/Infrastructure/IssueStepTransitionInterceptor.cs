using Foundry.Modules.Issues.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Foundry.Modules.Issues.Infrastructure;

/// <summary>
/// Adds one <see cref="IssueStepTransition"/> for every <see cref="Issue"/> a save adds, so the row commits or rolls
/// back with the state it records. Every write path that adds an issue (a transition, a newly detected issue) is
/// covered without knowing about the log (ADR 0123).
/// </summary>
internal sealed class IssueStepTransitionInterceptor : SaveChangesInterceptor
{
    public static IssueStepTransitionInterceptor Instance { get; } = new();

    private IssueStepTransitionInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RecordAddedIssues(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RecordAddedIssues(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void RecordAddedIssues(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (EntityEntry<Issue> entry in context.ChangeTracker.Entries<Issue>().Where(e => e.State == EntityState.Added).ToList())
        {
            Issue issue = entry.Entity;
            string state = entry.Metadata.GetDiscriminatorValue() as string
                ?? throw new InvalidOperationException($"Issue state {entry.Metadata.ClrType.Name} has no discriminator.");
            context.Add(IssueStepTransition.Entered(issue, state));
        }
    }
}
