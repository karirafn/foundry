namespace Foundry.Modules.Settings.Contracts;

public sealed record GlobalSettingsSummary(
    string AuthMode,
    int MaxConcurrent,
    int TimeoutMinutes,
    bool AccessTokenPresent,
    bool RefreshTokenPresent,
    DateTimeOffset? ExpiresAt,
    string? SubscriptionType,
    string? SystemPromptTemplate,
    string? WorkerPromptTemplate);
