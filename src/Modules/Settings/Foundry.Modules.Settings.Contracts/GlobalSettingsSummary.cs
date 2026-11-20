using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Settings.Contracts;

public sealed record GlobalSettingsSummary(
    int MaxConcurrent,
    int TimeoutMinutes,
    int ProbeIntervalMinutes,
    int PollIntervalSeconds,
    DateTimeOffset? UsageLimitResetsAt,
    bool IsDispatchPaused,
    bool AutoResumeOnUsageReset,
    string ImageTag,
    bool BuildEnabled,
    bool NestedDockerEnabled,
    ImageBuildStatus ImageBuildStatus,
    string? LastImageBuildError,
    bool HasUsableImage,
    DateTimeOffset? NextRetryAt,
    int Attempt,
    IReadOnlyList<string> AllowedProviderHosts,
    int MemoryLimitMb,
    double CpuLimit,
    int PidsLimit,
    IReadOnlyList<string> AdditionalDenyRules,
    IReadOnlyList<PausedHost> PausedHosts,
    int WebhookPort,
    string? WebhookPublicBaseUrl = null,
    int BackstopIntervalMinutes = 15,
    int AwaitingInputReminderDelayHours = 72,
    PausedPackageProxy? PackageProxyPause = null);
