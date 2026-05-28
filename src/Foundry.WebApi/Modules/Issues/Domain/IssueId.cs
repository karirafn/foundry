using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Issues.Domain;

public readonly record struct IssueId(Guid Value) : IStronglyTypedId<IssueId>
{
    public static IssueId New() => new(Guid.NewGuid());

    public static IssueId From(Guid value) => new(value);
}
