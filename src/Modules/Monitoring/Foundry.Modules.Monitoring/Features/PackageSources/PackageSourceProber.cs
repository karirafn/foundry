using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>Probes whether Foundry can read a Package Source with a credential.</summary>
internal interface IPackageSourceProber
{
    Task<PackageSourceVerdict.Probed> ProbeAsync(
        Uri serviceIndexUrl,
        UpstreamCredential? credential,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads a source the way NuGet restore starts to: <c>GET</c> the service index, which must answer 2xx with a NuGet v3
/// index naming a <c>SearchQueryService</c> resource, then <c>GET &lt;that resource&gt;?q=&amp;take=1</c>, which must
/// answer 2xx. The search proves the credential reaches the packages, not just the index. Any other outcome is
/// Unreadable with the failing status and a one-line reason.
/// </summary>
internal sealed class PackageSourceProber(PackageSourceUpstreamClient upstreamClient) : IPackageSourceProber
{
    /// <summary>How long one probe (both requests) may take before it counts as a transport failure.</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The largest service index read; a NuGet v3 index is a few kilobytes.</summary>
    internal const int MaxServiceIndexBytes = 1024 * 1024;

    internal const string NotAServiceIndexReason = "not a NuGet v3 service index";

    private const int MaxReasonLength = 300;
    private const string SearchQueryServiceType = "SearchQueryService";

    public async Task<PackageSourceVerdict.Probed> ProbeAsync(
        Uri serviceIndexUrl,
        UpstreamCredential? credential,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            return await ProbeCoreAsync(serviceIndexUrl, credential, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unreadable(httpStatus: null, "probe timed out");
        }
    }

    private async Task<PackageSourceVerdict.Probed> ProbeCoreAsync(
        Uri serviceIndexUrl,
        UpstreamCredential? credential,
        CancellationToken cancellationToken)
    {
        Result<HttpResponseMessage> indexResult = await upstreamClient.SendAsync(
            serviceIndexUrl,
            UpstreamMethod.Get,
            serviceIndexUrl,
            credential,
            cancellationToken);

        if (indexResult is Result<HttpResponseMessage>.Failure indexFailure)
        {
            return Unreadable(httpStatus: null, $"service index request failed: {indexFailure.Error.Message}");
        }

        Uri searchUrl;
        using (HttpResponseMessage index = ((Result<HttpResponseMessage>.Success)indexResult).Value)
        {
            int indexStatus = (int)index.StatusCode;
            if (!index.IsSuccessStatusCode)
            {
                return Unreadable(indexStatus, $"service index returned {indexStatus}");
            }

            if (await ReadSearchUrlAsync(index.Content, cancellationToken) is not { } found)
            {
                return Unreadable(indexStatus, NotAServiceIndexReason);
            }

            searchUrl = WithSearchQuery(found);
        }

        Result<HttpResponseMessage> searchResult = await upstreamClient.SendAsync(
            serviceIndexUrl,
            UpstreamMethod.Get,
            searchUrl,
            credential,
            cancellationToken);

        if (searchResult is Result<HttpResponseMessage>.Failure searchFailure)
        {
            return Unreadable(httpStatus: null, $"search request failed: {searchFailure.Error.Message}");
        }

        using HttpResponseMessage search = ((Result<HttpResponseMessage>.Success)searchResult).Value;
        int searchStatus = (int)search.StatusCode;

        return search.IsSuccessStatusCode
            ? new PackageSourceVerdict.Readable()
            : Unreadable(searchStatus, $"search returned {searchStatus}");
    }

    /// <summary>
    /// The <c>@id</c> of the first <c>resources</c> entry whose <c>@type</c> starts with <c>SearchQueryService</c>,
    /// or null when the body is not such an index.
    /// </summary>
    private static async Task<Uri?> ReadSearchUrlAsync(HttpContent content, CancellationToken cancellationToken)
    {
        byte[]? body = await ReadBoundedAsync(content, cancellationToken);
        if (body is null)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("resources", out JsonElement resources)
                || resources.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement resource in resources.EnumerateArray())
            {
                if (resource.ValueKind == JsonValueKind.Object
                    && resource.TryGetProperty("@type", out JsonElement type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString()!.StartsWith(SearchQueryServiceType, StringComparison.Ordinal)
                    && resource.TryGetProperty("@id", out JsonElement id)
                    && id.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(id.GetString(), UriKind.Absolute, out Uri? searchUrl)
                    && searchUrl.Scheme is ("http" or "https"))
                {
                    return searchUrl;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxServiceIndexBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static Uri WithSearchQuery(Uri searchUrl)
    {
        UriBuilder builder = new(searchUrl);
        string existing = builder.Query.TrimStart('?');
        builder.Query = existing.Length == 0 ? "q=&take=1" : $"{existing}&q=&take=1";
        return builder.Uri;
    }

    private static PackageSourceVerdict.Unreadable Unreadable(int? httpStatus, string reason)
    {
        string oneLine = reason.ReplaceLineEndings(" ").Trim();
        return new PackageSourceVerdict.Unreadable(
            httpStatus,
            oneLine.Length <= MaxReasonLength ? oneLine : oneLine[..MaxReasonLength]);
    }
}
