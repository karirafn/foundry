using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public readonly record struct CredentialId(Guid Value) : IStronglyTypedId<CredentialId>
{
    public static CredentialId New() => new(Guid.NewGuid());

    public static CredentialId From(Guid value) => new(value);
}
