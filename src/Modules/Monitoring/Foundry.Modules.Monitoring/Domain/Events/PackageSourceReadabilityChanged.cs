using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Events;

/// <summary>
/// A probe of the Package Source moved one credential's verdict between readable and not readable (Unchecked
/// counts as not readable), so the eligibility of every repository declaring the source may have changed.
/// </summary>
internal sealed record PackageSourceReadabilityChanged(PackageSourceId PackageSourceId, Uri ServiceIndexUrl) : IDomainEvent;
