namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>
/// Holds the role catalog of the current worker image. The image build publishes it from the same build-context tar
/// the image is built from, so dispatch and the role cards always see the roles the workers actually run (ADR 0095).
/// </summary>
internal interface IWorkerRoleCatalogStore
{
    /// <summary>The catalog of the current worker image, or <see langword="null"/> before the first build completes.</summary>
    WorkerRoleCatalog? Current { get; }

    void Publish(WorkerRoleCatalog catalog);
}

internal sealed class WorkerRoleCatalogStore : IWorkerRoleCatalogStore
{
    private WorkerRoleCatalog? _current;

    public WorkerRoleCatalog? Current => Volatile.Read(ref _current);

    public void Publish(WorkerRoleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Volatile.Write(ref _current, catalog);
    }
}
