namespace Foundry.Modules.Monitoring.Features.Providers.RootDirectory;

/// <summary>The answer to a root directory listing made with the previous listing's ETag.</summary>
internal abstract record RootDirectoryListing
{
    private RootDirectoryListing() { }

    /// <summary>The directory is unchanged since the listing that returned the ETag sent (GitHub HTTP 304).</summary>
    internal sealed record Unchanged : RootDirectoryListing;

    /// <summary>
    /// Every root entry. <see cref="ETag"/> is the value to send with the next listing, or null when the provider
    /// does not support conditional listings.
    /// </summary>
    internal sealed record Listed(IReadOnlyList<RootEntry> Entries, string? ETag) : RootDirectoryListing;
}
