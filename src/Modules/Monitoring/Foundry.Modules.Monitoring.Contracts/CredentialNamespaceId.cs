using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

public readonly record struct CredentialNamespaceId(Guid Value) : IStronglyTypedId<CredentialNamespaceId>
{
    public static CredentialNamespaceId New() => new(Guid.NewGuid());

    public static CredentialNamespaceId From(Guid value) => new(value);
}
