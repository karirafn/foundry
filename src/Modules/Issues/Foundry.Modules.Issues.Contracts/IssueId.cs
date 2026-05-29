using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

public readonly record struct IssueId(Guid Value) : IStronglyTypedId<IssueId>
{
    public static IssueId New() => new(Guid.NewGuid());

    public static IssueId From(Guid value) => new(value);
}
