using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.RootDirectory;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    private const string SymlinkMode = "120000";

    /// <summary>
    /// Lists the default branch's root tree (<c>GET /repos/{owner}/{repo}/git/trees/HEAD</c>, not recursive),
    /// sending <paramref name="ifNoneMatch"/> as <c>If-None-Match</c>. A 304 answers
    /// <see cref="RootDirectoryListing.Unchanged"/> and does not count against GitHub's primary rate limit.
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
            return Result<RootDirectoryListing>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/git/trees/HEAD");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);
        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            RecordRestHeadroom(response);
            return Result<RootDirectoryListing>.Ok(new RootDirectoryListing.Unchanged());
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<RootDirectoryListing>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubTreeDto? dto = JsonSerializer.Deserialize<GitHubTreeDto>(body, JsonOptions);

        List<RootEntry> entries = [];
        foreach (GitHubTreeEntryDto entry in dto?.Tree ?? [])
        {
            if (entry.Path is null || entry.Sha is null || ToRootEntryKind(entry) is not { } kind)
            {
                continue;
            }

            entries.Add(new RootEntry(entry.Path, kind, entry.Sha));
        }

        return Result<RootDirectoryListing>.Ok(
            new RootDirectoryListing.Listed(entries, response.Headers.ETag?.ToString()));
    }

    /// <summary>Reads one blob's raw content (<c>GET /repos/{owner}/{repo}/git/blobs/{sha}</c>).</summary>
    public async Task<Result<string>> ReadBlobAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string blobSha,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string sha = Uri.EscapeDataString(blobSha);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/git/blobs/{sha}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        return Result<string>.Ok(content);
    }

    private static RootEntryKind? ToRootEntryKind(GitHubTreeEntryDto entry) =>
        entry.Type switch
        {
            "blob" when entry.Mode == SymlinkMode => RootEntryKind.Symlink,
            "blob" => RootEntryKind.File,
            "tree" => RootEntryKind.Directory,
            "commit" => RootEntryKind.Submodule,
            _ => null,
        };

    private sealed record GitHubTreeDto(IReadOnlyList<GitHubTreeEntryDto>? Tree);

    private sealed record GitHubTreeEntryDto(string? Path, string? Mode, string? Type, string? Sha);
}
