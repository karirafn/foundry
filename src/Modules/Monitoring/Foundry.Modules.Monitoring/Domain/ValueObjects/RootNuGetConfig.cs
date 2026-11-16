namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// What a repository's root <c>nuget.config</c> on its default branch declares, as last read by the poll.
/// </summary>
internal abstract record RootNuGetConfig
{
    internal const string MoreThanOneConfigMessage = "more than one root nuget.config";

    private RootNuGetConfig() { }

    /// <summary>The repository has no root <c>nuget.config</c>, so it declares no Package Sources.</summary>
    internal sealed record Absent : RootNuGetConfig;

    /// <summary>The root config parsed; <see cref="Sources"/> are its Package Sources (possibly none).</summary>
    internal sealed record Declared(
        string BlobSha,
        string RawText,
        IReadOnlyList<DeclaredPackageSource> Sources) : RootNuGetConfig;

    /// <summary>
    /// The root config could not be read as a source list: it is not well-formed XML, or the root holds more
    /// than one file NuGet could read as <c>nuget.config</c> (then <see cref="BlobSha"/> and
    /// <see cref="RawText"/> are null, because there is no single file to keep).
    /// </summary>
    internal sealed record Invalid(string? BlobSha, string? RawText, string Message) : RootNuGetConfig;

    internal static Invalid MoreThanOneConfig() => new(BlobSha: null, RawText: null, MoreThanOneConfigMessage);
}
