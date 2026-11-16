using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.RootDirectory;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    private const int RootEntriesPerPage = 100;
    private const int MaxRootDirectoryPages = 20;
    private const string SymlinkMode = "120000";

    /// <summary>
    /// Lists the default branch's root directory (<c>GET /projects/:id/repository/tree</c>, not recursive),
    /// following pagination. GitLab offers no conditional listing here, so <paramref name="ifNoneMatch"/> is not
    /// sent and the listing never answers <see cref="RootDirectoryListing.Unchanged"/>.
    /// </summary>
    public async Task<Result<RootDirectoryListing>> ListRootDirectoryAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string? ifNoneMatch,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<RootDirectoryListing>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        List<RootEntry> entries = [];

        for (int page = 1; page <= MaxRootDirectoryPages; page++)
        {
            string relativePath = $"projects/{encodedPath}/repository/tree?per_page={RootEntriesPerPage}&page={page}";
            Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            AddCommonHeaders(request, token);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result<RootDirectoryListing>.Fail(ErrorFromNonSuccess(response));
            }

            RecordRestHeadroom(response);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            List<GitLabTreeEntryDto> pageItems = JsonSerializer.Deserialize<List<GitLabTreeEntryDto>>(body, JsonOptions) ?? [];

            foreach (GitLabTreeEntryDto entry in pageItems)
            {
                if (entry.Name is null || entry.Id is null || ToRootEntryKind(entry) is not { } kind)
                {
                    continue;
                }

                entries.Add(new RootEntry(entry.Name, kind, entry.Id));
            }

            if (!HasNextPage(response, pageItems.Count))
            {
                return Result<RootDirectoryListing>.Ok(new RootDirectoryListing.Listed(entries, ETag: null));
            }
        }

        return Result<RootDirectoryListing>.Fail(GitLabErrors.RootDirectoryTooLarge(MaxRootDirectoryPages * RootEntriesPerPage));
    }

    /// <summary>Reads one blob's raw content (<c>GET /projects/:id/repository/blobs/:sha/raw</c>).</summary>
    public async Task<Result<string>> ReadBlobAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string blobSha,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        string sha = Uri.EscapeDataString(blobSha);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/repository/blobs/{sha}/raw");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        return Result<string>.Ok(content);
    }

    /// <summary>
    /// GitLab names the next page in <c>X-Next-Page</c> (empty on the last page). Without that header, only a
    /// full page can have a successor.
    /// </summary>
    private static bool HasNextPage(HttpResponseMessage response, int pageCount)
    {
        if (response.Headers.TryGetValues("X-Next-Page", out IEnumerable<string>? values))
        {
            return !string.IsNullOrWhiteSpace(values.FirstOrDefault());
        }

        return pageCount >= RootEntriesPerPage;
    }

    private static RootEntryKind? ToRootEntryKind(GitLabTreeEntryDto entry) =>
        entry.Type switch
        {
            "blob" when entry.Mode == SymlinkMode => RootEntryKind.Symlink,
            "blob" => RootEntryKind.File,
            "tree" => RootEntryKind.Directory,
            "commit" => RootEntryKind.Submodule,
            _ => null,
        };

    private sealed record GitLabTreeEntryDto(string? Id, string? Name, string? Type, string? Mode);
}
