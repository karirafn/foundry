namespace Foundry.Modules.Settings.Contracts;

public sealed record GlobalSettingsSummary(
    int MaxConcurrent,
    int TimeoutMinutes,
    string? SystemPromptTemplate,
    string? WorkerPromptTemplate,
    DateTimeOffset? UsageLimitResetsAt,
    bool IsDispatchPaused,
    bool AutoResumeOnUsageReset,
    bool InstallDotnet,
    bool InstallAngular,
    bool InstallGlab,
    bool InstallGh,
    bool InstallChromium,
    bool InstallDocker,
    ImageBuildStatus ImageBuildStatus,
    string? LastImageBuildError,
    bool HasUsableImage,
    DateTimeOffset? NextRetryAt,
    int Attempt);
