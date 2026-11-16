using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.RootDirectory;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Keeps a repository's declared Package Sources in step with the root <c>nuget.config</c> on its default
/// branch: one root listing per poll, and a file read only when the config's blob SHA changed. A failed listing or
/// read leaves the stored state (ETag included) as it was, so a provider hiccup never clears the declared sources
/// and the next poll lists again in full.
/// </summary>
internal static class RootNuGetConfigRefresh
{
    internal const string ConfigFileName = "nuget.config";

    public static async Task RefreshAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Result<RootDirectoryListing> listingResult = await provider.ListRootDirectoryAsync(
            repository.Slug,
            repository.RootListingETag,
            cancellationToken);

        if (listingResult is Result<RootDirectoryListing>.Failure listingFailure)
        {
            LogFailure(logger, repository, "list the root directory", listingFailure.Error);
            return;
        }

        if (((Result<RootDirectoryListing>.Success)listingResult).Value is not RootDirectoryListing.Listed listed)
        {
            return;
        }

        List<RootEntry> configs = listed.Entries
            .Where(entry => entry.Kind == RootEntryKind.File
                && string.Equals(entry.Name, ConfigFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Result<RootNuGetConfig> configResult = configs switch
        {
            [] => new RootNuGetConfig.Absent(),
            [RootEntry single] when single.BlobSha == repository.RootNuGetConfigBlobSha => repository.RootNuGetConfig,
            [RootEntry single] => await ReadConfigAsync(repository, provider, single, cancellationToken),
            _ => RootNuGetConfig.MoreThanOneConfig(),
        };

        if (configResult is Result<RootNuGetConfig>.Failure readFailure)
        {
            LogFailure(logger, repository, "read the root nuget.config", readFailure.Error);
            return;
        }

        repository.RecordRootListing(((Result<RootNuGetConfig>.Success)configResult).Value, listed.ETag);
    }

    private static async Task<Result<RootNuGetConfig>> ReadConfigAsync(
        MonitoredRepository repository,
        IIssueProvider provider,
        RootEntry entry,
        CancellationToken cancellationToken)
    {
        Result<string> readResult = await provider.ReadBlobAsync(repository.Slug, entry.BlobSha, cancellationToken);

        return readResult.Match(
            text => Result<RootNuGetConfig>.Ok(NuGetConfigParser.Parse(entry.BlobSha, text)),
            Result<RootNuGetConfig>.Fail);
    }

    private static void LogFailure(ILogger logger, MonitoredRepository repository, string action, Error error)
    {
        // A provider without root reads has nothing to report; its repositories simply declare no sources.
        if (error.Code == RootDirectoryErrors.NotSupportedCode)
        {
            return;
        }

        logger.LogWarning(
            "Could not {Action} of repository {Slug}; its declared package sources are left as they were: {Error}",
            action,
            repository.Slug,
            error.Message);
    }
}
