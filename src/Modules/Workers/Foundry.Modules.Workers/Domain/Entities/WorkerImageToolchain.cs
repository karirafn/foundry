using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Domain.Entities;

/// <summary>
/// The toolchain one worker image reported when Foundry probed it, keyed by the image ID (ADR 0109). A rebuild yields a
/// new image ID and so a new row; earlier rows stay, a few hundred bytes each.
/// </summary>
public sealed class WorkerImageToolchain
{
    /// <summary>Longer than a <c>sha256:</c> image ID, the only form Docker reports.</summary>
    public const int MaxImageIdLength = 128;

    // Private parameterless constructor for EF Core materialization.
    private WorkerImageToolchain()
    {
        ImageId = string.Empty;
        Toolchain = Toolchain.Unrecorded;
    }

    private WorkerImageToolchain(string imageId, Toolchain toolchain, DateTimeOffset recordedAt)
    {
        ImageId = imageId;
        Toolchain = toolchain;
        RecordedAt = recordedAt;
    }

    public string ImageId { get; private set; }

    public Toolchain Toolchain { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public static WorkerImageToolchain Create(string imageId, Toolchain toolchain, DateTimeOffset recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(imageId.Length, MaxImageIdLength, nameof(imageId));
        ArgumentNullException.ThrowIfNull(toolchain);

        return new WorkerImageToolchain(imageId, toolchain, recordedAt);
    }
}
