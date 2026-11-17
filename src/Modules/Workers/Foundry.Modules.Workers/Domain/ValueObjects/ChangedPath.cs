namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>One path a reviewed commit changed, exactly as git printed it (a quoted form for unusual characters).</summary>
public sealed record ChangedPath
{
    public ChangedPath(ChangeStatus status, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A changed path is added, modified or deleted.");
        }

        Status = status;
        Path = path;
    }

    public ChangeStatus Status { get; }

    public string Path { get; }
}
