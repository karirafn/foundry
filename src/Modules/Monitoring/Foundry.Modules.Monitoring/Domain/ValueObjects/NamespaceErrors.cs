using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

public static class NamespaceErrors
{
    public static readonly Error InvalidFormat = new(
        "Namespace.InvalidFormat",
        "Namespace must be one or more non-empty path segments separated by '/', " +
        "using only alphanumeric characters, hyphens, underscores, and dots. " +
        "Segments '.' and '..' are forbidden.");
}
