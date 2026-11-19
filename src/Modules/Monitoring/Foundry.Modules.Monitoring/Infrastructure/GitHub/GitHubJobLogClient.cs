using System.Net;
using System.Net.Http.Headers;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>
/// Downloads GitHub Actions job logs. <c>GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs</c> answers with a
/// redirect to a short-lived (one minute) download URL on a storage host. The client this class is built with must
/// not follow redirects itself: this class follows the redirect so it can drop the token, which must never reach the
/// storage host. Only the log's final <see cref="JobLogTailReader.JobLogTailBytes"/> are read, since a log can run
/// to hundreds of megabytes.
/// </summary>
internal sealed class GitHubJobLogClient(HttpClient httpClient)
{
    private const string ApiVersion = "2026-03-10";

    public async Task<Result<string>> GetJobLogAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        long jobId,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/actions/jobs/{jobId}/logs");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return Result<string>.Ok(await JobLogTailReader.ReadAsync(response.Content, false, cancellationToken));
        }

        if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
        {
            return Result<string>.Fail(JobLogErrors.Unavailable(jobId, (int)response.StatusCode));
        }

        Uri downloadUri = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        if (downloadUri.Scheme is not "https")
        {
            return Result<string>.Fail(JobLogErrors.Unavailable(jobId, (int)response.StatusCode));
        }

        // The download URL expires about a minute after the redirect, so the probe and the tail read follow at once.
        return await ReadLogTailAsync(downloadUri, jobId, cancellationToken);
    }

    // The storage host ignores the suffix form "bytes=-N" (it answers 200 with the whole log), so the log's length
    // is probed first and the tail is requested from its start offset.
    private async Task<Result<string>> ReadLogTailAsync(Uri downloadUri, long jobId, CancellationToken cancellationToken)
    {
        long totalLength;
        using (HttpResponseMessage probe = await SendDownloadAsync(downloadUri, new RangeHeaderValue(0, 0), cancellationToken))
        {
            if (probe.StatusCode is HttpStatusCode.OK)
            {
                return Result<string>.Ok(await JobLogTailReader.ReadAsync(probe.Content, false, cancellationToken));
            }

            if (probe.StatusCode is not (HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable))
            {
                return Result<string>.Fail(JobLogErrors.Unavailable(jobId, (int)probe.StatusCode));
            }

            if (probe.StatusCode is not HttpStatusCode.PartialContent || ProbedLength(probe) is not long probedLength)
            {
                return await ReadWholeLogTailAsync(downloadUri, jobId, cancellationToken);
            }

            totalLength = probedLength;
        }

        if (totalLength == 0)
        {
            return Result<string>.Ok(string.Empty);
        }

        long start = Math.Max(0, totalLength - JobLogTailReader.JobLogTailBytes);
        using HttpResponseMessage tail = await SendDownloadAsync(
            downloadUri, new RangeHeaderValue(start, null), cancellationToken);

        return tail.StatusCode switch
        {
            HttpStatusCode.PartialContent =>
                Result<string>.Ok(await JobLogTailReader.ReadAsync(tail.Content, start > 0, cancellationToken)),
            HttpStatusCode.OK =>
                Result<string>.Ok(await JobLogTailReader.ReadAsync(tail.Content, false, cancellationToken)),
            HttpStatusCode.RequestedRangeNotSatisfiable => await ReadWholeLogTailAsync(downloadUri, jobId, cancellationToken),
            _ => Result<string>.Fail(JobLogErrors.Unavailable(jobId, (int)tail.StatusCode)),
        };
    }

    private async Task<Result<string>> ReadWholeLogTailAsync(Uri downloadUri, long jobId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendDownloadAsync(downloadUri, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(JobLogErrors.Unavailable(jobId, (int)response.StatusCode));
        }

        return Result<string>.Ok(await JobLogTailReader.ReadAsync(response.Content, false, cancellationToken));
    }

    // The download URL is pre-signed: it carries its own short-lived authorization, so the request sends no
    // Authorization header and the token never leaves GitHub's API host.
    private async Task<HttpResponseMessage> SendDownloadAsync(
        Uri downloadUri,
        RangeHeaderValue? range,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage downloadRequest = new(HttpMethod.Get, downloadUri);
        downloadRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));
        downloadRequest.Headers.Range = range;

        return await httpClient.SendAsync(downloadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static long? ProbedLength(HttpResponseMessage probe) =>
        probe.Content.Headers.ContentRange is { Unit: "bytes", Length: long length } ? length : null;

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Found
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string uriString = uri.ToString();
        return uriString.EndsWith('/') ? uri : new Uri(uriString + '/');
    }
}
