using Microsoft.AspNetCore.Http;

namespace Foundry.Shared.Infrastructure.Http.Ingress;

/// <summary>
/// One port-scoped ingress (ADR 0085): a dedicated Kestrel port that serves only paths under
/// <see cref="PathPrefix"/>, while no other port serves that prefix. An ingress whose <see cref="Port"/> is
/// not configured still claims its prefix, so the prefix is served on no port at all.
/// </summary>
public sealed record IngressEntry
{
    public IngressEntry(int? port, PathString pathPrefix)
    {
        if (port is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(port));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 65535, nameof(port));
        }

        // A root prefix would claim every path, leaving the main port nothing to serve.
        if (!pathPrefix.HasValue || pathPrefix.Value == "/")
        {
            throw new ArgumentException("An ingress path prefix must name at least one path segment.", nameof(pathPrefix));
        }

        Port = port;
        PathPrefix = pathPrefix;
    }

    public int? Port { get; }

    public PathString PathPrefix { get; }

    internal bool Claims(PathString path) => path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);
}
