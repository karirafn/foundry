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
/// storage host.
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

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return Result<string>.Ok(await response.Content.ReadAsStringAsync(cancellationToken));
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

        // The download URL is pre-signed: it carries its own short-lived authorization, so the request
        // sends no Authorization header and the token never leaves GitHub's API host.
        using HttpRequestMessage downloadRequest = new(HttpMethod.Get, downloadUri);
        downloadRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage downloadResponse = await httpClient.SendAsync(downloadRequest, cancellationToken);

        if (!downloadResponse.IsSuccessStatusCode)
        {
            return Result<string>.Fail(JobLogErrors.Unavailable(jobId, (int)downloadResponse.StatusCode));
        }

        return Result<string>.Ok(await downloadResponse.Content.ReadAsStringAsync(cancellationToken));
    }

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
