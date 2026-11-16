using Microsoft.AspNetCore.Http;

namespace Foundry.Shared.Infrastructure.Http.Ingress;

/// <summary>
/// The port → path-prefix table behind the ingress port guard (ADR 0085). The WebApi is unauthenticated,
/// so each ingress port must expose nothing but its own prefix, and no other port may serve that prefix.
/// </summary>
public sealed class IngressPortTable
{
    private readonly IReadOnlyList<IngressEntry> _entries;

    public IngressPortTable(IEnumerable<IngressEntry> entries)
    {
        List<IngressEntry> list = [.. entries];

        int? duplicatePort = list.Where(e => e.Port is not null).GroupBy(e => e.Port).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicatePort is { } port)
        {
            throw new InvalidOperationException($"Two ingress entries are configured on port {port}; each ingress needs its own port.");
        }

        _entries = list;
    }

    /// <summary>Whether <paramref name="localPort"/> is one of the ingress ports.</summary>
    public bool IsIngressPort(int localPort) => _entries.Any(e => e.Port == localPort);

    /// <summary>
    /// Whether a request for <paramref name="path"/> arriving on <paramref name="localPort"/> may be served:
    /// an ingress port serves only its own prefix, and a prefixed path is served only on its entry's port.
    /// </summary>
    public bool Allows(int localPort, PathString path)
    {
        IngressEntry? portEntry = _entries.FirstOrDefault(e => e.Port == localPort);
        if (portEntry is not null && !portEntry.Claims(path))
        {
            return false;
        }

        return _entries.All(e => e.Port == localPort || !e.Claims(path));
    }
}
