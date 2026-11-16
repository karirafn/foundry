using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The credential a Package Source was probed with. A source keeps one verdict per probe credential, because one
/// account may read a source another cannot.
/// </summary>
internal abstract record ProbeCredential
{
    private const string AnonymousKey = "anonymous";
    private const string AccountKeyPrefix = "account:";

    private ProbeCredential() { }

    /// <summary>No credential: the source's host is not the repository's provider host.</summary>
    internal sealed record Anonymous : ProbeCredential
    {
        public override string Key => AnonymousKey;
    }

    /// <summary>The account credential that covers the repository's namespace, on the source's own host.</summary>
    internal sealed record Account(CredentialId CredentialId) : ProbeCredential
    {
        public override string Key => AccountKeyPrefix + CredentialId.Value.ToString("D");
    }

    /// <summary>The persisted form, unique per credential.</summary>
    public abstract string Key { get; }
}
