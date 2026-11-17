namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The kind of worker run an issue is running, or will run when it is next dispatched (#1059). Declared by the issue
/// state and shown as the board's lead tag. The last four split a revision by its revision trigger. A run kind always
/// implies the worker role: <see cref="Triage"/> runs the triage role, every other kind the implement role.
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
}
