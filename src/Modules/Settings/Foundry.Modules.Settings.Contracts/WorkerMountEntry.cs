namespace Foundry.Modules.Settings.Contracts;

public sealed record WorkerMountEntry(string ContainerPath, string HostPath, bool ReadOnly);
