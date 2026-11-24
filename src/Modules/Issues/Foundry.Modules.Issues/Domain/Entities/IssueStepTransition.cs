using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.ValueObjects;

namespace Foundry.Modules.Issues.Domain.Entities;

/// <summary>
/// One entry of an issue's step transition log: the issue entered <see cref="State"/> at <see cref="EnteredAt"/>, on
/// <see cref="LifecycleStep"/> in <see cref="Round"/>, because of <see cref="Cause"/>. Written for every added
/// <see cref="Issue"/> in the save that adds it, kept for the issue's lifetime and deleted with it.
/// </summary>
public sealed class IssueStepTransition
{
    // Private parameterless constructor for EF Core materialization.
    private IssueStepTransition()
    {
    }

    /// <summary>The store-generated position of the entry in the log, so entries read back in the order they were written.</summary>
    public long Sequence { get; private set; }

    public IssueId IssueId { get; private set; }

    /// <summary>The state's wire token, as the issues table's <c>state</c> column stores it.</summary>
    public string State { get; private set; } = string.Empty;

    public IssueLifecycleStep LifecycleStep { get; private set; }

    public int Round { get; private set; }

    public DateTimeOffset EnteredAt { get; private set; }

    /// <summary>The state's <see cref="Issue.TransitionCause"/>, or <see langword="null"/> for plain progress.</summary>
    public string? Cause { get; private set; }

    internal static IssueStepTransition Entered(Issue issue, string state)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        return new IssueStepTransition
        {
            IssueId = issue.Id,
            State = state,
            LifecycleStep = issue.LifecycleStep,
            Round = issue.Round,
            EnteredAt = issue.EnteredStateAt,
            Cause = issue.TransitionCause,
        };
    }
}
