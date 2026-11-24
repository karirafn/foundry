using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

internal sealed partial class GitHubHttpClient
{
    private const int WorkflowRunJobsPerPage = 100;

    /// <summary>
    /// Lists the latest attempt's jobs of workflow run <paramref name="runId"/>
    /// (<c>GET /repos/{owner}/{repo}/actions/runs/{run_id}/jobs?filter=latest</c>), first page only.
    /// </summary>
    public async Task<Result<IReadOnlyList<WorkflowRunJob>>> ListWorkflowRunJobsAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        long runId,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result<IReadOnlyList<WorkflowRunJob>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/actions/runs/{runId}/jobs?filter=latest&per_page={WorkflowRunJobsPerPage}";
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<WorkflowRunJob>>.Fail(ErrorFromNonSuccess(response));
        }

        RecordRestHeadroom(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        GitHubWorkflowRunJobsDto? dto = JsonSerializer.Deserialize<GitHubWorkflowRunJobsDto>(body, JsonOptions);

        List<WorkflowRunJob> jobs = [];
        foreach (GitHubWorkflowRunJobDto job in dto?.Jobs ?? [])
        {
            jobs.Add(new WorkflowRunJob(job.Id, job.Name ?? string.Empty, job.HtmlUrl));
        }

        return Result<IReadOnlyList<WorkflowRunJob>>.Ok(jobs);
    }

    /// <summary>
    /// Re-runs the Actions job <paramref name="jobId"/> (<c>POST /repos/{owner}/{repo}/actions/jobs/{job_id}/rerun</c>).
    /// </summary>
    public async Task<Result> RerunJobAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        long jobId,
        string token,
        CancellationToken cancellationToken)
    {
        if (apiBaseUrl.Scheme is not "https")
        {
            return Result.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        Uri requestUri = new(EnsureTrailingSlash(apiBaseUrl), $"repos/{owner}/{repo}/actions/jobs/{jobId}/rerun");

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        AddGitHubHeaders(request, token);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail(CiJobRetryErrors.Refused(jobId, (int)response.StatusCode));
        }

        RecordRestHeadroom(response);
        return Result.Ok();
    }

    private sealed record GitHubWorkflowRunJobsDto(IReadOnlyList<GitHubWorkflowRunJobDto>? Jobs);

    private sealed record GitHubWorkflowRunJobDto(long Id, string? Name, string? HtmlUrl);
}
