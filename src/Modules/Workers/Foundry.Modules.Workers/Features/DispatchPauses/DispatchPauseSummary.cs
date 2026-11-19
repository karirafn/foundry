using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Workers.Features.DispatchPauses;

/// <summary>
/// Applies one transition to the tracked <see cref="DispatchPause"/> for the pause endpoints, which answer with the
/// settings summary the dashboard reads.
/// </summary>
internal static class DispatchPauseSummary
{
    public static async Task<Result<GlobalSettingsSummary>> ApplyAsync(
        DbContext dbContext,
        IGlobalSettingsQueries settingsQueries,
        Func<DispatchPause, Task> transition,
        CancellationToken cancellationToken)
    {
        GlobalSettingsSummary? summary = await settingsQueries.GetSettingsAsync(cancellationToken);

        if (summary is null)
        {
            return Result<GlobalSettingsSummary>.Fail(DispatchPauseErrors.SettingsNotFound);
        }

        DispatchPause? pause = await dbContext.FindDispatchPauseAsync(cancellationToken);

        if (pause is null)
        {
            return Result<GlobalSettingsSummary>.Fail(DispatchPauseErrors.NotFound);
        }

        await transition(pause);
        await dbContext.SaveChangesAsync(cancellationToken);

        DispatchPauseState state = DispatchPauseQueries.ToState(pause);

        return summary with
        {
            UsageLimitResetsAt = state.UsageLimitResetsAt,
            IsDispatchPaused = state.IsDispatchPaused,
            PausedHosts = state.PausedHosts,
            PackageProxyPause = state.PackageProxyPause,
        };
    }
}
