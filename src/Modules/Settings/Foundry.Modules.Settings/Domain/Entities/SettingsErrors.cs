using Foundry.Modules.Settings.Domain.ValueObjects;
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
        new(InvalidPromptTemplateTooLongCode, $"Prompt templates must not exceed {WorkerPrompts.MaxPromptTemplateLength} characters.");

    internal const string WorkerPromptMissingIssueNumberCode = "Settings.WorkerPromptMissingIssueNumber";

    internal static readonly Error WorkerPromptMissingIssueNumber =
        new(WorkerPromptMissingIssueNumberCode, "The worker prompt template must contain the {issueNumber} placeholder.");

    internal const string InvalidBranchNamingInstructionCode = "Settings.InvalidBranchNamingInstruction";

    internal static readonly Error InvalidBranchNamingInstruction =
        new(InvalidBranchNamingInstructionCode, "Branch naming instructions must not contain line break or null characters.");

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

    internal const string MountContainerPathNotAbsoluteCode = "Settings.MountContainerPathNotAbsolute";

    internal static Error MountContainerPathNotAbsolute(string path) =>
        new(MountContainerPathNotAbsoluteCode, $"Mount container path '{path}' must be absolute (start with '/').");

    internal const string MountHostPathNotAbsoluteCode = "Settings.MountHostPathNotAbsolute";

    internal static Error MountHostPathNotAbsolute(string path) =>
        new(MountHostPathNotAbsoluteCode, $"Mount host path '{path}' must be absolute.");

    internal const string MountPathTraversalCode = "Settings.MountPathTraversal";

    internal static Error MountPathTraversal(string path) =>
        new(MountPathTraversalCode, $"Mount path '{path}' must not contain path traversal segments (..).");

    internal const string InvalidMountPathCharactersCode = "Settings.InvalidMountPathCharacters";

    internal static Error InvalidMountPathCharacters(string path) =>
        new(InvalidMountPathCharactersCode, $"Mount path '{path}' contains a separator or control character that is not allowed.");

    internal const string SensitiveMountContainerPathCode = "Settings.SensitiveMountContainerPath";

    internal static Error SensitiveMountContainerPath(string path) =>
        new(SensitiveMountContainerPathCode, $"Mount container path '{path}' targets a sensitive system directory and is not allowed.");

    internal const string SensitiveMountHostPathCode = "Settings.SensitiveMountHostPath";

    internal static Error SensitiveMountHostPath(string path) =>
        new(SensitiveMountHostPathCode, $"Mount host path '{path}' targets a sensitive system directory and is not allowed.");

    internal const string ModelTooLongCode = "Settings.ModelTooLong";

    internal static readonly Error ModelTooLong =
        new(ModelTooLongCode, $"The Claude Code model must not exceed {ClaudeCodeSettings.MaxModelLength} characters.");

    internal const string InvalidDenyRuleCode = "Settings.InvalidDenyRule";

    internal static Error InvalidDenyRule(string rule) =>
        new(InvalidDenyRuleCode, $"Deny rule '{rule}' must have the form Tool(pattern), where Tool is one of Bash, Edit, Read, Write, MultiEdit, Glob, or Grep.");

    internal const string HostSettingsPathNotAbsoluteCode = "Settings.HostSettingsPathNotAbsolute";

    internal static Error HostSettingsPathNotAbsolute(string path) =>
        new(HostSettingsPathNotAbsoluteCode, $"Host settings file path '{path}' must be absolute (/… or a drive path such as C:\\…).");

    internal const string HostSettingsPathTraversalCode = "Settings.HostSettingsPathTraversal";

    internal static Error HostSettingsPathTraversal(string path) =>
        new(HostSettingsPathTraversalCode, $"Host settings file path '{path}' must not contain path traversal segments (..).");

    internal const string InvalidHostSettingsPathCharactersCode = "Settings.InvalidHostSettingsPathCharacters";

    internal static Error InvalidHostSettingsPathCharacters(string path) =>
        new(InvalidHostSettingsPathCharactersCode, $"Host settings file path '{path}' contains a separator or control character that is not allowed.");

    internal const string SensitiveHostSettingsPathCode = "Settings.SensitiveHostSettingsPath";

    internal static Error SensitiveHostSettingsPath(string path) =>
        new(SensitiveHostSettingsPathCode, $"Host settings file path '{path}' targets a sensitive system directory and is not allowed.");

    internal const string HostSettingsPathTooLongCode = "Settings.HostSettingsPathTooLong";

    internal static readonly Error HostSettingsPathTooLong =
        new(HostSettingsPathTooLongCode, $"The host settings file path must not exceed {HostHooks.MaxHostSettingsPathLength} characters.");

    internal const string EmptyExcludedHookCommandCode = "Settings.EmptyExcludedHookCommand";

    internal static readonly Error EmptyExcludedHookCommand =
        new(EmptyExcludedHookCommandCode, "Excluded hook commands must not be empty or whitespace.");

    internal const string ExcludedHookCommandTooLongCode = "Settings.ExcludedHookCommandTooLong";

    internal static readonly Error ExcludedHookCommandTooLong =
        new(ExcludedHookCommandTooLongCode, $"Excluded hook commands must not exceed {HostHooks.MaxExcludedCommandLength} characters.");

    internal const string TooManyExcludedHookCommandsCode = "Settings.TooManyExcludedHookCommands";

    internal static Error TooManyExcludedHookCommands(int count) =>
        new(TooManyExcludedHookCommandsCode, $"The hook exclusion list may not exceed {HostHooks.MaxExcludedCommandCount} entries, but {count} were provided.");

    internal const string DuplicateMountContainerPathCode = "Settings.DuplicateMountContainerPath";

    internal static Error DuplicateMountContainerPath(string path) =>
        new(DuplicateMountContainerPathCode, $"Container path '{path}' appears more than once. Each container path may be mounted only once.");
}
