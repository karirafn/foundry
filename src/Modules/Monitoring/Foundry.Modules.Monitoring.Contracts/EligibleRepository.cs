namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A repository confirmed eligible for dispatch, with its priority position, concurrency limit and provider
/// host. Lower position means higher dispatch priority. <see cref="DeclaresPackageSources"/> is read at selection time,
/// so a package-proxy pause holds the repository only while its root <c>nuget.config</c> declares Package Sources.
/// </summary>
public sealed record EligibleRepository(
    Guid Id,
    int Position,
    int MaxConcurrentWorkers,
    string Host,
    bool DeclaresPackageSources = false);
