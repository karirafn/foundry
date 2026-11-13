using Foundry.Modules.Settings.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.HostPauses;

/// <summary>
/// Pauses a provider host on the tracked <see cref="GlobalSettings"/> without saving, so the pause commits in the same
/// save as the run failure that found the host unreachable.
/// </summary>
internal static class HostPauseStaging
{
    /// <summary>
    /// Returns <c>true</c> when this added the host's pause; <c>false</c> when the host was already paused (its backoff
    /// is left alone) or no settings row exists.
    /// </summary>
    public static async Task<bool> StageAsync(
        DbContext dbContext,
        string host,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>().FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning("The provider host {Host} is unreachable, but no GlobalSettings row exists to pause it on.", host);
            return false;
        }

        return settings.PauseHost(host, now);
    }
}
