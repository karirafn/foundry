using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Shared;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

internal sealed partial class GitLabHttpClient
{
    private const string FailedPipelineStatus = "failed";
    private const int FailedJobsPerPage = 100;

    // GitLab bounds a job name at 255 characters. The caps protect the stored failing-check payloads (outbox
    // message, revision trigger) from a misbehaving server. An over-long URL is dropped rather than cut, since a
    // cut URL would resolve to a different page.
    private const int MaxJobNameLength = 255;
    private const int MaxWebUrlLength = 2048;

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

    /// <summary>
    /// Classifies a merge request's head pipeline as checks. A failed pipeline's failed jobs that must pass
    /// (<c>allow_failure</c> false) become its failing checks (<c>GET /projects/:id/pipelines/:pipeline_id/jobs</c>).
    /// </summary>
    public async Task<Result<Checks>> GetPipelineChecksAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        long? pipelineId,
        string? pipelineStatus,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<Checks>.Fail(GitLabErrors.InvalidBaseUrl);
        }

        if (pipelineStatus is not FailedPipelineStatus)
        {
            return Result<Checks>.Ok(Checks.FromGitLab(pipelineStatus, []));
        }

        if (pipelineId is not long failedPipelineId)
        {
            logger.LogWarning(
                "GitLab head pipeline of {Repository} is failed but carries no id; its failed jobs cannot be read.",
                slug.FullPath);
            return Result<Checks>.Ok(Checks.FromGitLab(pipelineStatus, []));
        }

        string encodedPath = Uri.EscapeDataString(slug.FullPath);
        Uri requestUri = new(
            EnsureTrailingSlash(apiBaseUrl),
            $"projects/{encodedPath}/pipelines/{failedPipelineId}/jobs?scope[]=failed&per_page={FailedJobsPerPage}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddCommonHeaders(request, token);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            return await ClassifyFailedJobsAsync(response, slug, failedPipelineId, pipelineStatus, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Result<Checks>.Fail(GitLabErrors.ProviderError(
                $"The GitLab jobs read for pipeline {failedPipelineId} could not complete: {ex.GetType().Name}."));
        }
    }

    private async Task<Result<Checks>> ClassifyFailedJobsAsync(
        HttpResponseMessage response,
        RepositorySlug slug,
        long failedPipelineId,
        string pipelineStatus,
        CancellationToken cancellationToken)
    {
        if (IsAuthFailure(response))
        {
            return Result<Checks>.Ok(new Checks.Unreadable());
        }

        if (IsPlainClientError(response))
        {
            RecordRestHeadroom(response);
            logger.LogWarning(
                "GitLab answered {StatusCode} reading the failed jobs of pipeline {PipelineId} in {Repository};"
                + " the failing checks cannot be named.",
                (int)response.StatusCode,
                failedPipelineId,
                slug.FullPath);
            return Result<Checks>.Ok(Checks.FromGitLab(pipelineStatus, []));
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result<Checks>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        IReadOnlyList<GitLabJobDto> jobs;
        try
        {
            jobs = JsonSerializer.Deserialize<List<GitLabJobDto>>(body, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            return Result<Checks>.Fail(GitLabErrors.ProviderError(
                $"The GitLab jobs read for pipeline {failedPipelineId} could not complete: {ex.GetType().Name}."));
        }

        if (jobs.Count == FailedJobsPerPage)
        {
            logger.LogWarning(
                "GitLab returned a full page of failed jobs for pipeline {PipelineId} in {Repository};"
                + " further failed jobs may be unnamed.",
                failedPipelineId,
                slug.FullPath);
        }

        List<FailingCheck> failingChecks = jobs
            .Where(job => !job.AllowFailure)
            .Select(ToFailingCheck)
            .OfType<FailingCheck>()
            .ToList();

        return Result<Checks>.Ok(Checks.FromGitLab(pipelineStatus, failingChecks));
    }

    private static FailingCheck? ToFailingCheck(GitLabJobDto job)
    {
        string name = Sanitize(job.Name, MaxJobNameLength);
        string status = Sanitize(job.Status, MaxJobNameLength);

        if (name.Length == 0 || status.Length == 0)
        {
            return null;
        }

        string? webUrl = job.WebUrl?.Length > MaxWebUrlLength ? null : job.WebUrl;
        return new FailingCheck(name, status, webUrl);
    }

    // Job names are free text a CI file author controls (a YAML key may hold a newline), and they land inside the
    // dispatched prompt's line-oriented failing-checks block, so each control character becomes a space.
    private static string Sanitize(string? text, int maxLength)
    {
        if (text is null)
        {
            return string.Empty;
        }

        string flattened = string.Create(
            text.Length,
            text,
            static (span, source) =>
            {
                for (int index = 0; index < source.Length; index++)
                {
                    span[index] = char.IsControl(source[index]) ? ' ' : source[index];
                }
            });

        string trimmed = flattened.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static bool IsAuthFailure(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Unauthorized
        || (response.StatusCode is HttpStatusCode.Forbidden && !IsRateLimited(response));

    private static bool IsPlainClientError(HttpResponseMessage response) =>
        (int)response.StatusCode is >= 400 and < 500
        && response.StatusCode is not (
            HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.TooManyRequests);

    private sealed record GitLabJobDto(string Name, string Status, bool AllowFailure, string? WebUrl);
}
