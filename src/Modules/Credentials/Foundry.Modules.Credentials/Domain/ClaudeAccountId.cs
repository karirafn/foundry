using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain;

public readonly record struct ClaudeAccountId(Guid Value) : IStronglyTypedId<ClaudeAccountId>
{
    public static readonly ClaudeAccountId Default = new(new Guid("00000000-0000-0000-0000-000000000002"));

    public static ClaudeAccountId New() => new(Guid.NewGuid());

    public static ClaudeAccountId From(Guid value) => new(value);
}
