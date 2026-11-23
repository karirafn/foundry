using System.Net;
using System.Net.Http.Headers;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>
/// Downloads GitHub Actions job logs. <c>GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs</c> answers with a
/// redirect to a short-lived (one minute) download URL on a storage host. The client this class is built with must
/// not follow redirects itself: this class follows the redirect so it can drop the token, which must never reach the
/// storage host. Only the log's final <see cref="JobLogTailReader.JobLogTailBytes"/> and its first
/// <see cref="FailureExcerptScanner.ScanBytes"/> are read, since a log can run to hundreds of megabytes.
/// </summary>
internal sealed class GitHubJobLogClient(HttpClient httpClient)
{
    private const string ApiVersion = "2026-03-10";

    public async Task<Result<CiJobLog.Available>> GetJobLogAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        long jobId,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<CiJobLog.Available>.Fail(GitHubErrors.InvalidBaseUrl);
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
            return Result<CiJobLog.Available>.Ok(await ReadWholeLogAsync(response.Content, cancellationToken));
        }

        if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
        {
            return Result<CiJobLog.Available>.Fail(JobLogErrors.Unavailable(jobId, (int)response.StatusCode));
        }

        Uri downloadUri = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        if (downloadUri.Scheme is not "https")
        {
            return Result<CiJobLog.Available>.Fail(JobLogErrors.Unavailable(jobId, (int)response.StatusCode));
        }

        // The download URL expires about a minute after the redirect, so the probe and the reads follow at once.
        return await ReadDownloadAsync(downloadUri, jobId, cancellationToken);
    }

    // The storage host ignores the suffix form "bytes=-N" (it answers 200 with the whole log), so the log's length
    // is probed first and the tail is requested from its start offset. A log no larger than the scan cap is read
    // whole in one request; a larger one has its head read in a second range request, after the tail.
    private async Task<Result<CiJobLog.Available>> ReadDownloadAsync(
        Uri downloadUri,
        long jobId,
        CancellationToken cancellationToken)
    {
        long totalLength;
        using (HttpResponseMessage probe = await SendDownloadAsync(downloadUri, new RangeHeaderValue(0, 0), cancellationToken))
        {
            if (probe.StatusCode is HttpStatusCode.OK)
            {
                return Result<CiJobLog.Available>.Ok(await ReadWholeLogAsync(probe.Content, cancellationToken));
            }

            if (probe.StatusCode is not (HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable))
            {
                return Result<CiJobLog.Available>.Fail(JobLogErrors.Unavailable(jobId, (int)probe.StatusCode));
            }

            if (probe.StatusCode is not HttpStatusCode.PartialContent || ProbedLength(probe) is not long probedLength)
            {
                return await DownloadWholeLogAsync(downloadUri, jobId, cancellationToken);
            }

            totalLength = probedLength;
        }

        if (totalLength == 0)
        {
            return Result<CiJobLog.Available>.Ok(new CiJobLog.Available(string.Empty, 0, []));
        }

        long start = totalLength <= FailureExcerptScanner.ScanBytes
            ? 0
            : totalLength - JobLogTailReader.JobLogTailBytes;
        JobLogTail tail;
        using (HttpResponseMessage tailResponse = await SendDownloadAsync(
            downloadUri, new RangeHeaderValue(start, null), cancellationToken))
        {
            switch (tailResponse.StatusCode)
            {
                case HttpStatusCode.OK:
                    return Result<CiJobLog.Available>.Ok(await ReadWholeLogAsync(tailResponse.Content, cancellationToken));
                case HttpStatusCode.RequestedRangeNotSatisfiable:
                    return await DownloadWholeLogAsync(downloadUri, jobId, cancellationToken);
                case HttpStatusCode.PartialContent when start == 0:
                    return Result<CiJobLog.Available>.Ok(await ReadWholeLogAsync(tailResponse.Content, cancellationToken));
                case HttpStatusCode.PartialContent:
                    tail = await JobLogTailReader.ReadAsync(tailResponse.Content, start, null, cancellationToken);
                    break;
                default:
                    return Result<CiJobLog.Available>.Fail(JobLogErrors.Unavailable(jobId, (int)tailResponse.StatusCode));
            }
        }

        IReadOnlyList<JobLogLine> excerpt = await ReadHeadExcerptAsync(downloadUri, cancellationToken);
        return Result<CiJobLog.Available>.Ok(new CiJobLog.Available(tail.Text, tail.Length, excerpt));
    }

    // The tail is already read, so a head the storage host will not serve leaves only the excerpt empty.
    private async Task<IReadOnlyList<JobLogLine>> ReadHeadExcerptAsync(Uri downloadUri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage head = await SendDownloadAsync(
            downloadUri, new RangeHeaderValue(0, FailureExcerptScanner.ScanBytes - 1), cancellationToken);

        return head.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent
            ? await FailureExcerptScanner.ScanAsync(head.Content, cancellationToken)
            : [];
    }

    private async Task<Result<CiJobLog.Available>> DownloadWholeLogAsync(
        Uri downloadUri,
        long jobId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendDownloadAsync(downloadUri, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<CiJobLog.Available>.Fail(JobLogErrors.Unavailable(jobId, (int)response.StatusCode));
        }

        return Result<CiJobLog.Available>.Ok(await ReadWholeLogAsync(response.Content, cancellationToken));
    }

    private static async Task<CiJobLog.Available> ReadWholeLogAsync(HttpContent content, CancellationToken cancellationToken)
    {
        FailureExcerptScanner scanner = new();
        JobLogTail tail = await JobLogTailReader.ReadAsync(content, 0, scanner, cancellationToken);
        return new CiJobLog.Available(tail.Text, tail.Length, scanner.Complete());
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
