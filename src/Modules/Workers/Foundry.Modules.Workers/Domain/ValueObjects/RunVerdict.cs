namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// How a worker run's schema-validated verdict (ADR 0094) was read from its final result event.
/// Recorded only for a run that exited 0 and whose role has a verdict.
/// </summary>
public enum RunVerdictStatus
{
    /// <summary>The role reported its work done.</summary>
    Done,

    /// <summary>The role needs answers from a person before the work can continue.</summary>
    NeedsInput,

    /// <summary>The final result event carried no <c>structured_output</c>.</summary>
    Missing,

    /// <summary>The <c>structured_output</c> failed the role's typed verdict parse.</summary>
    Invalid,
}

/// <summary>
/// One question a run asks a person, with the context it needs and the one option it recommends (the shared
/// <c>questions</c> definition). <paramref name="Recommendation"/> is null only for a question recorded before runs
/// returned recommendations.
/// </summary>
public sealed record RunVerdictQuestion(string Question, string Context, string? Recommendation);

/// <summary>The verdict a worker run returned, as Foundry recorded it.</summary>
public sealed record RunVerdict
{
    private RunVerdict(RunVerdictStatus status, IReadOnlyList<RunVerdictQuestion> questions)
    {
        Status = status;
        Questions = questions;
    }

    public RunVerdictStatus Status { get; }

    /// <summary>The run's questions; non-empty only for <see cref="RunVerdictStatus.NeedsInput"/>.</summary>
    public IReadOnlyList<RunVerdictQuestion> Questions { get; }

    public static RunVerdict Done() => new(RunVerdictStatus.Done, []);

    public static RunVerdict Missing() => new(RunVerdictStatus.Missing, []);

    public static RunVerdict Invalid() => new(RunVerdictStatus.Invalid, []);

    public static RunVerdict NeedsInput(IReadOnlyList<RunVerdictQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            throw new ArgumentException("A needs-input verdict must carry at least one question.", nameof(questions));
        }

        return new RunVerdict(RunVerdictStatus.NeedsInput, [.. questions]);
    }

    /// <summary>Rehydrates a verdict recorded on a worker run.</summary>
    internal static RunVerdict FromStored(RunVerdictStatus status, IReadOnlyList<RunVerdictQuestion>? questions) =>
        new(status, questions is null ? [] : [.. questions]);

    public bool Equals(RunVerdict? other) =>
        other is not null && Status == other.Status && Questions.SequenceEqual(other.Questions);

    public override int GetHashCode() => HashCode.Combine(Status, Questions.Count);
}
