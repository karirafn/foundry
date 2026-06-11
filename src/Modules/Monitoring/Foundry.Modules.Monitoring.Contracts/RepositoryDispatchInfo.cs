namespace Foundry.Modules.Monitoring.Contracts;

public sealed record RepositoryDispatchInfo(
    string RepositorySlug,
    Uri CloneUrl,
    string? AccountToken);
