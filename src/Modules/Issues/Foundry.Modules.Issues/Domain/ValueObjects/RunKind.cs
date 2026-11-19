namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The kind of worker run an issue is running, or will run when it is next dispatched (#1059). Declared by the issue
/// state and shown as the board's lead tag. The last four split a revision by its revision trigger. A run kind always
/// implies the worker role: <see cref="Triage"/> runs the triage role, <see cref="Review"/> the review role, and every
/// other kind the implement role.
/// </summary>
public enum RunKind
{
    Triage,
    Fresh,
    Continuation,
    ReviewFix,
    ChecksFix,
    ConflictFix,
    InputResume,

    /// <summary>The review role judges a green pull request before it merges or reaches a person (ADR 0113).</summary>
    Review,
}
