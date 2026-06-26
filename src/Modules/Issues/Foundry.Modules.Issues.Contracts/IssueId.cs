using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public readonly record struct IssueId(Guid Value) : IStronglyTypedId<IssueId>, IComparable<IssueId>
{
    public static IssueId New() => new(Guid.NewGuid());

    public static IssueId From(Guid value) => new(value);

    // The id column is stored as TEXT in SQLite (Guid "D" format, e.g. "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx").
    // SQLite sorts TEXT columns using ordinal string order, so the C# comparison must use the same
    // ordering — otherwise i.Id > cursorId in the keyset query diverges from the in-memory sort.
    public int CompareTo(IssueId other) =>
        string.CompareOrdinal(Value.ToString("D"), other.Value.ToString("D"));

    // These operators support EF Core keyset pagination (i.Id > cursorId / ThenBy(i => i.Id))
    // and are aligned to the persisted TEXT column ordering so C# and SQL comparisons agree.
    public static bool operator <(IssueId left, IssueId right) => left.CompareTo(right) < 0;

    public static bool operator >(IssueId left, IssueId right) => left.CompareTo(right) > 0;

    public static bool operator <=(IssueId left, IssueId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(IssueId left, IssueId right) => left.CompareTo(right) >= 0;
}
