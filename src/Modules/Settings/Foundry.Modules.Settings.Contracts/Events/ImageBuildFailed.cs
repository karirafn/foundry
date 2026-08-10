using Foundry.Shared;

namespace Foundry.Modules.Settings.Contracts;

public sealed record ImageBuildFailed(
    string? ErrorTail,
    DateTimeOffset? NextRetryAt,
    int Attempt) : IIntegrationEvent;
