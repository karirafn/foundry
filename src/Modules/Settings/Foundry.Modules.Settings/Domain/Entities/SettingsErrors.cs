using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.Entities;

internal static class SettingsErrors
{
    internal const string NotFoundCode = "Settings.NotFound";
    internal const string InvalidMaxConcurrentCode = "Settings.InvalidMaxConcurrent";
    internal const string InvalidTimeoutCode = "Settings.InvalidTimeout";
    internal const string InvalidPromptTemplateCode = "Settings.InvalidPromptTemplate";
    internal const string InvalidPromptTemplateTooLongCode = "Settings.InvalidPromptTemplateTooLong";

    internal static readonly Error NotFound =
        new(NotFoundCode, "Global settings were not found.");

    internal static readonly Error InvalidPromptTemplate =
        new(InvalidPromptTemplateCode, "Prompt templates must not be empty. Provide a value or omit the field to use the default.");

    internal static readonly Error InvalidPromptTemplateTooLong =
        new(InvalidPromptTemplateTooLongCode, $"Prompt templates must not exceed {GlobalSettings.MaxPromptTemplateLength} characters.");

    internal static Error InvalidMaxConcurrent(int value) =>
        new(InvalidMaxConcurrentCode, $"Max concurrent workers must be between 1 and 20, but was {value}.");

    internal static Error InvalidTimeout(int value) =>
        new(InvalidTimeoutCode, $"Timeout must be between 1 and 1440 minutes, but was {value}.");

    internal const string InvalidRetryStatusCode = "Settings.InvalidRetryStatus";

    internal static readonly Error InvalidRetryStatus =
        new(InvalidRetryStatusCode, "Retry is only valid when the last build failed.");

    internal const string InvalidProbeIntervalCode = "Settings.InvalidProbeInterval";

    internal static Error InvalidProbeInterval(int value) =>
        new(InvalidProbeIntervalCode, $"Probe interval must be between {GlobalSettings.MinProbeIntervalMinutes} and {GlobalSettings.MaxProbeIntervalMinutes} minutes, but was {value}.");
}
