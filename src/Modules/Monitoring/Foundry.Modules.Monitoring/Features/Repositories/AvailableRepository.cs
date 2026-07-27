namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// A repository returned by the provider that may be added to monitoring.
/// </summary>
internal sealed record AvailableRepository(string Slug, bool IsPrivate, bool CanPush, bool IsMonitored);
