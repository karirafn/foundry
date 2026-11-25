using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

public sealed record ImageBuildOutcomeFailed(
    string? ErrorTail,
    DateTimeOffset? NextRetryAt,
    int Attempt,
    ImageRebuildOrigin Origin = ImageRebuildOrigin.Requested) : IIntegrationEvent;
