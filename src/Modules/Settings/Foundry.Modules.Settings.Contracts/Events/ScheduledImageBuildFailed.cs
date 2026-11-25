using Foundry.Shared;

namespace Foundry.Modules.Settings.Contracts;

public sealed record ScheduledImageBuildFailed(
    string? ErrorTail,
    DateTimeOffset? NextRetryAt,
    int Attempt) : IIntegrationEvent;
