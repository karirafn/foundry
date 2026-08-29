using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Accounts.Tokens;

/// <summary>
/// Discriminated union returned by <see cref="TokenAccountResolver.ResolveAsync"/> to carry the
/// structured result of token validation and namespace derivation without encoding outcomes into
/// <see cref="Error.Message"/>.
/// </summary>
internal abstract class TokenResolution
{
    private TokenResolution() { }

    /// <summary>Token validated; account name and derived namespaces are ready to apply.</summary>
    internal sealed class Resolved(string accountName, IReadOnlyCollection<Namespace> namespaces) : TokenResolution
    {
        public string AccountName { get; } = accountName;
        public IReadOnlyCollection<Namespace> Namespaces { get; } = namespaces;
    }

    /// <summary>
    /// The token's entire derived namespace set is already fully claimed by other credentials.
    /// The error carries the server-composed message naming each namespace and its holder.
    /// </summary>
    internal sealed class ClaimedElsewhere(Error error) : TokenResolution
    {
        public Error Error { get; } = error;
    }

    /// <summary>Token validation or namespace derivation failed; the error carries the reason.</summary>
    internal sealed class Rejected(Error error) : TokenResolution
    {
        public Error Error { get; } = error;
    }
}
