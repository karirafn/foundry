namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>The facts about the machine Foundry's process runs on that decide how a worker reaches it.</summary>
internal interface IFoundryHostSystem
{
    /// <summary>The kernel release text (<c>/proc/sys/kernel/osrelease</c>), or <see langword="null"/> when unreadable.</summary>
    string? ReadKernelOsRelease();

    IReadOnlyList<HostNetworkInterface> ListNetworkInterfaces();
}
