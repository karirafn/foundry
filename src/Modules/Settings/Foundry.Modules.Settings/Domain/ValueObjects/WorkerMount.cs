namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed record WorkerMount(string ContainerPath, string HostPath, bool ReadOnly);
