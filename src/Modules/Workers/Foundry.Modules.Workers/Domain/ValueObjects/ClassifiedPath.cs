namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>A changed path with the class <see cref="ChangedFileClassifier"/> gave it when the review was dispatched.</summary>
public sealed record ClassifiedPath(ChangedPath Path, ChangedFileClass Class)
{
    public static ClassifiedPath Of(ChangedPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new ClassifiedPath(path, ChangedFileClassifier.Classify(path.Path));
    }

    /// <summary>Whether the change adds or modifies the path: a deletion changes no behaviour a test must cover.</summary>
    public bool IsAddedOrModified => Path.Status is ChangeStatus.Added or ChangeStatus.Modified;
}
