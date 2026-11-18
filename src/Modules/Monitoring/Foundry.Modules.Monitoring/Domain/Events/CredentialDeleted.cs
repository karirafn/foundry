using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Events;

/// <summary>An account was deleted, so it no longer counts towards anything derived from its token.</summary>
internal sealed record CredentialDeleted(CredentialId CredentialId) : IDomainEvent;
