using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.Entities;

internal static class SettingsErrors
{
    internal const string NotFoundCode = "Settings.NotFound";
    internal const string InvalidMaxConcurrentCode = "Settings.InvalidMaxConcurrent";
    internal const string InvalidTimeoutCode = "Settings.InvalidTimeout";

    internal static readonly Error NotFound =
        new(NotFoundCode, "Global settings were not found.");

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

    internal const string InvalidPollIntervalCode = "Settings.InvalidPollInterval";

    internal static Error InvalidPollInterval(int value) =>
        new(InvalidPollIntervalCode, $"Poll interval must be between {GlobalSettings.MinPollIntervalSeconds} and {GlobalSettings.MaxPollIntervalSeconds} seconds, but was {value}.");

    internal const string InvalidBackstopIntervalCode = "Settings.InvalidBackstopInterval";

    internal static Error InvalidBackstopInterval(int value) =>
        new(InvalidBackstopIntervalCode, $"Backstop interval must be between {GlobalSettings.MinBackstopIntervalMinutes} and {GlobalSettings.MaxBackstopIntervalMinutes} minutes, but was {value}.");

    internal const string InvalidWebhookPublicBaseUrlCode = "Settings.InvalidWebhookPublicBaseUrl";

    internal static readonly Error InvalidWebhookPublicBaseUrl =
        new(InvalidWebhookPublicBaseUrlCode, $"The webhook public base URL must be an absolute https URL without query or fragment, at most {GlobalSettings.MaxWebhookPublicBaseUrlLength} characters.");

    internal const string InvalidProviderHostCode = "Settings.InvalidProviderHost";

    internal static Error InvalidProviderHost(string host) =>
        new(InvalidProviderHostCode, $"'{host}' is not a valid provider host. Provide a bare hostname (e.g. git.example.com) with no scheme, port, or path.");

    internal const string EmptyImageTagCode = "Settings.EmptyImageTag";

    internal static readonly Error EmptyImageTag =
        new(EmptyImageTagCode, "The worker image tag must not be empty or whitespace.");

    internal const string LatestImageTagCode = "Settings.LatestImageTag";

    internal static readonly Error LatestImageTag =
        new(LatestImageTagCode, "The worker image tag must not end with ':latest'. Provide a specific tag.");

    internal const string ImageTagTooLongCode = "Settings.ImageTagTooLong";

    internal static readonly Error ImageTagTooLong =
        new(ImageTagTooLongCode, $"The worker image tag must not exceed {WorkerImageConfiguration.MaxImageTagLength} characters.");

    internal const string TooManyProviderHostsCode = "Settings.TooManyProviderHosts";

    internal static Error TooManyProviderHosts(int count) =>
        new(TooManyProviderHostsCode, $"The allowed provider hosts list may not exceed {GlobalSettings.MaxAllowedProviderHostCount} entries, but {count} were provided.");

    internal const string ProviderHostTooLongCode = "Settings.ProviderHostTooLong";

    internal static Error ProviderHostTooLong(string host) =>
        new(ProviderHostTooLongCode, $"'{host[..Math.Min(host.Length, 40)]}…' exceeds the maximum allowed host length of {GlobalSettings.MaxProviderHostLength} characters.");

    internal const string InvalidMemoryLimitCode = "Settings.InvalidMemoryLimit";

    internal static Error InvalidMemoryLimit(int value) =>
        new(InvalidMemoryLimitCode, $"Worker memory limit must be between {WorkerResourceLimits.MinMemoryLimitMb} and {WorkerResourceLimits.MaxMemoryLimitMb} MB, but was {value}.");

    internal const string InvalidCpuLimitCode = "Settings.InvalidCpuLimit";

    internal static Error InvalidCpuLimit(double value) =>
        new(InvalidCpuLimitCode, $"Worker CPU limit must be between {WorkerResourceLimits.MinCpuLimit} and {WorkerResourceLimits.MaxCpuLimit} CPUs, but was {value}.");

    internal const string InvalidPidsLimitCode = "Settings.InvalidPidsLimit";

    internal static Error InvalidPidsLimit(int value) =>
        new(InvalidPidsLimitCode, $"Worker PIDs limit must be between {WorkerResourceLimits.MinPidsLimit} and {WorkerResourceLimits.MaxPidsLimit}, but was {value}.");

    internal const string InvalidDenyRuleCode = "Settings.InvalidDenyRule";

    internal static Error InvalidDenyRule(string rule) =>
        new(InvalidDenyRuleCode, $"Deny rule '{rule}' must have the form Tool(pattern), where Tool is one of Bash, Edit, Read, Write, MultiEdit, Glob, or Grep.");
}
