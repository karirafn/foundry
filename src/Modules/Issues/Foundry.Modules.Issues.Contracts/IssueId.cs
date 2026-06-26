using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public readonly record struct IssueId(Guid Value) : IStronglyTypedId<IssueId>, IComparable<IssueId>
{
    public static IssueId New() => new(Guid.NewGuid());

    public static IssueId From(Guid value) => new(value);

    public int CompareTo(IssueId other) => Value.CompareTo(other.Value);

    public static bool operator <(IssueId left, IssueId right) => left.CompareTo(right) < 0;

    public static bool operator >(IssueId left, IssueId right) => left.CompareTo(right) > 0;

    public static bool operator <=(IssueId left, IssueId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(IssueId left, IssueId right) => left.CompareTo(right) >= 0;
}
