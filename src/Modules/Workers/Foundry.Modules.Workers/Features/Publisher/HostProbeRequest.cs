namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>One repository to probe, with the token Prepare would use for it.</summary>
internal sealed record HostProbeRequest(Uri CloneUrl, string GitPat);
