using System.Net.Http.Headers;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    /// <summary>Reads the log of CI job <paramref name="jobId"/> (<c>GET /projects/:id/jobs/:job_id/trace</c>).</summary>
    public async Task<Result<string>> GetJobTraceAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        long jobId,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<string>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"projects/{encodedPath}/jobs/{jobId}/trace");

        // The trace is plain text, so the JSON Accept header of the other endpoints is not sent.
        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Add("PRIVATE-TOKEN", token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<string>.Fail(JobLogErrors.Unavailable(jobId, (int)response.StatusCode));
        }

        RecordRestHeadroom(response);
        return Result<string>.Ok(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
