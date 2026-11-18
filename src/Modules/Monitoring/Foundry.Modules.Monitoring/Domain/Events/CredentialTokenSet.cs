using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Events;

/// <summary>
/// An account token was set: the account was created with a token, or its token was replaced. The new
/// token carries its own expiry, so anything derived from the old one is stale.
/// </summary>
internal sealed record CredentialTokenSet(CredentialId CredentialId) : IDomainEvent;
