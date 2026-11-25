namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Why a worker image rebuild ran. A failed <see cref="Scheduled"/> rebuild leaves dispatch running on the current
/// verified image; a failed <see cref="Requested"/> one (startup, settings change, manual retry) stops it (ADR 0126).
/// </summary>
public enum ImageRebuildOrigin
{
    Requested,
    Scheduled,
}
