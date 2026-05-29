using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.Shared;

namespace Foundry.WebApi.Modules.Monitoring.Infrastructure;

internal sealed class GitHubHttpClient(HttpClient httpClient)
{
    private const string ApiVersion = "2026-03-10";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        Uri baseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        if (baseUrl.Scheme is not ("https" or "http"))
        {
            return Result<IReadOnlyList<ProviderIssue>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string relativePath = $"repos/{Uri.EscapeDataString(slug.Owner)}/{Uri.EscapeDataString(slug.Name)}/issues?labels=foundry&state=open";
        Uri requestUri = new(baseUrl, relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<ProviderIssue>>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubIssueDto>? dtos = JsonSerializer.Deserialize<List<GitHubIssueDto>>(body, JsonOptions);

        IReadOnlyList<ProviderIssue> issues = (dtos ?? [])
            .Select(dto => new ProviderIssue(
                Number: dto.Number,
                Title: dto.Title,
                Body: dto.Body ?? string.Empty,
                Author: dto.User.Login,
                Url: dto.HtmlUrl,
                Labels: dto.Labels
                    .Select(l => l.Name)
                    .ToList()))
            .ToList();

        return Result<IReadOnlyList<ProviderIssue>>.Ok(issues);
    }

    public async Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
        Uri baseUrl,
        RepositorySlug slug,
        int issueNumber,
        string token,
        CancellationToken cancellationToken)
    {
        if (baseUrl.Scheme is not ("https" or "http"))
        {
            return Result<IReadOnlyList<int>>.Fail(GitHubErrors.InvalidBaseUrl);
        }

        string owner = Uri.EscapeDataString(slug.Owner);
        string repo = Uri.EscapeDataString(slug.Name);
        string relativePath = $"repos/{owner}/{repo}/issues/{issueNumber}/dependencies/blocked_by";
        Uri requestUri = new(baseUrl, relativePath);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<int>>.Fail(ErrorFromNonSuccess(response));
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GitHubDependencyDto>? dtos = JsonSerializer.Deserialize<List<GitHubDependencyDto>>(body, JsonOptions);

        string expectedFullName = $"{slug.Owner}/{slug.Name}";
        IReadOnlyList<int> issueNumbers = (dtos ?? [])
            .Where(dto => string.Equals(dto.Repository.FullName, expectedFullName, StringComparison.OrdinalIgnoreCase))
            .Select(dto => dto.Number)
            .ToList();

        return Result<IReadOnlyList<int>>.Ok(issueNumbers);
    }

    private static Error ErrorFromNonSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
                remaining.FirstOrDefault() == "0")
            {
                return GitHubErrors.RateLimitExhausted;
            }
        }

        int statusCode = (int)response.StatusCode;
        return GitHubErrors.UnexpectedStatusCode(statusCode);
    }

    private sealed record GitHubIssueDto(
        int Number,
        string Title,
        string? Body,
        GitHubUserDto User,
        string HtmlUrl,
        IReadOnlyList<GitHubLabelDto> Labels);

    private sealed record GitHubUserDto(string Login);

    private sealed record GitHubLabelDto(string Name);

    private sealed record GitHubDependencyDto(
        int Number,
        string Title,
        GitHubRepositoryDto Repository);

    private sealed record GitHubRepositoryDto(string FullName);
}

internal static class GitHubErrors
{
    public static readonly Error InvalidBaseUrl = new(
        "GitHub.InvalidBaseUrl",
        "The base URL must use the https or http scheme.");

    public static readonly Error RateLimitExhausted = new(
        "GitHub.RateLimitExhausted",
        "GitHub API rate limit exhausted. Wait before retrying.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitHub.UnexpectedStatusCode", $"GitHub API returned unexpected status code {statusCode}.");
}
