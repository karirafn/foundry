using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public readonly record struct IssueId(Guid Value) : IStronglyTypedId<IssueId>, IComparable<IssueId>
{
    public static IssueId New() => new(Guid.NewGuid());

    public static IssueId From(Guid value) => new(value);

    // IssueId is persisted by SQLite as TEXT in "D" format and compared ordinally by the database engine.
    // Using string.CompareOrdinal on the "D" representation guarantees that the C# ordering matches
    // SQLite's ORDER BY and keyset WHERE (i.Id > cursorId), preventing pagination gaps or duplicates.
    public int CompareTo(IssueId other) =>
        string.CompareOrdinal(Value.ToString("D"), other.Value.ToString("D"));

    // These operators support EF Core keyset pagination (i.Id > cursorId / ThenBy(i => i.Id))
    // and are aligned to the persisted TEXT column ordering so C# and SQL comparisons agree.
    public static bool operator <(IssueId left, IssueId right) => left.CompareTo(right) < 0;

    public static bool operator >(IssueId left, IssueId right) => left.CompareTo(right) > 0;

    public static bool operator <=(IssueId left, IssueId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(IssueId left, IssueId right) => left.CompareTo(right) >= 0;
}
