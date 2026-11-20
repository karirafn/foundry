using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.DispatchPauses;

internal static class DispatchPauseErrors
{
    internal const string NotFoundCode = "Workers.DispatchPauseNotFound";

    // The pause endpoints answer on the Settings routes and return the settings summary, so a missing settings row
    // keeps the error code those routes have always returned.
    internal const string SettingsNotFoundCode = "Settings.NotFound";

    internal static readonly Error NotFound =
        new(NotFoundCode, "The dispatch pause state was not found.");

    internal static readonly Error SettingsNotFound =
        new(SettingsNotFoundCode, "Global settings were not found.");
}
