using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public sealed class CredentialNamespace
{
    // Private parameterless constructor for EF Core materialization.
    private CredentialNamespace()
    {
    }

    private CredentialNamespace(CredentialNamespaceId id, CredentialId credentialId, string host, string value)
    {
        Id = id;
        CredentialId = credentialId;
        Host = host;
        Value = value;
    }

    public CredentialNamespaceId Id { get; private set; }

    public CredentialId CredentialId { get; private set; }

    public string Host { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    internal static CredentialNamespace Create(CredentialId credentialId, string host, Namespace ns)
    {
        return new CredentialNamespace(CredentialNamespaceId.New(), credentialId, host, ns.Value);
    }
}
