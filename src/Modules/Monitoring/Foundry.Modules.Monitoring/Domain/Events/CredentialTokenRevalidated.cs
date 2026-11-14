using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Events;

/// <summary>
/// An account token moved from Invalid to Valid: a recheck authenticated it, or a new token was set.
/// </summary>
internal sealed record CredentialTokenRevalidated(CredentialId CredentialId) : IDomainEvent;
