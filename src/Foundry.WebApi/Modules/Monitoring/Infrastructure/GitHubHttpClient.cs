using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Monitoring.Infrastructure;

internal sealed class GitHubHttpClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken)
    {
        string url = $"repos/{slug.Owner}/{slug.Name}/issues?labels=foundry&state=open";

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Foundry", null));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return HandleNonSuccess(response);
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

    private static Result<IReadOnlyList<ProviderIssue>> HandleNonSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
                remaining.FirstOrDefault() == "0")
            {
                return Result<IReadOnlyList<ProviderIssue>>.Fail(GitHubErrors.RateLimitExhausted);
            }
        }

        int statusCode = (int)response.StatusCode;
        return Result<IReadOnlyList<ProviderIssue>>.Fail(
            GitHubErrors.UnexpectedStatusCode(statusCode));
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
}

internal static class GitHubErrors
{
    public static readonly Error RateLimitExhausted = new(
        "GitHub.RateLimitExhausted",
        "GitHub API rate limit exhausted. Wait before retrying.");

    public static Error UnexpectedStatusCode(int statusCode) =>
        new("GitHub.UnexpectedStatusCode", $"GitHub API returned unexpected status code {statusCode}.");
}
