using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Events;

/// <summary>The provider's verdict on an account token moved from Valid to Invalid.</summary>
internal sealed record CredentialTokenInvalidated(CredentialId CredentialId) : IDomainEvent;
