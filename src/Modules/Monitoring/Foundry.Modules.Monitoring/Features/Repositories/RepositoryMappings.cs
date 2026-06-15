namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryMappings
{
    internal static int? ToSeconds(TimeSpan? interval) =>
        interval.HasValue ? (int)interval.Value.TotalSeconds : null;
}
