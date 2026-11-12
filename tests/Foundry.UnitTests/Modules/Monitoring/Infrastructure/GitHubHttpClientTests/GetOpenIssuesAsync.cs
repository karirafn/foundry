using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetOpenIssuesAsync
{
    private static readonly Uri GitHubComBaseUrl = new("https://api.github.com");

    // Captured 2026-11-12 with issues(first: 3, states: OPEN, orderBy: {field: CREATED_AT, direction: DESC})
    // { pageInfo { hasNextPage endCursor } nodes { number title } } on karirafn/foundry. The rateLimit block is
    // copied from the captured issueOrPullRequest(number: 847) response, as the query under test asks for it.
    private const string CapturedOpenIssues = """
        {"data":{"rateLimit":{"cost":1,"limit":5000,"remaining":4986,"resetAt":"2026-11-12T17:16:50Z"},"repository":{"issues":{"pageInfo":{"hasNextPage":true,"endCursor":"Y3Vyc29yOnYyOpK0MjAyNi0xMS0xMlQxNToyMjowMFrPAAAAAXqq0iI="},"nodes":[{"number":867,"title":"feat(monitoring): GitLab names a failed pipeline's failed jobs as failing checks"},{"number":866,"title":"fix(monitoring): GitHub failing checks are named, and a red verdict that names none goes straight to review"},{"number":865,"title":"fix(monitoring): a long comment reaches the run whole, and the prompt fits the clarification log instead of each comment being cut at 4,000 characters"}]}}}}
        """;

    private static RepositorySlug Slug => RepositorySlug.Create("karirafn/foundry").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler, InMemoryProviderRateBudget? budget = null)
    {
        HttpClient httpClient = new(handler);
        return new GitHubHttpClient(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            budget ?? new InMemoryProviderRateBudget(),
            TimeProvider.System);
    }

    [Fact]
    public async Task WhenGitHubListsOpenIssues_ReturnsTheirNumbersAndTitlesInResponseOrder()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, CapturedOpenIssues));

        // Act
        Result<IReadOnlyList<OpenIssue>> result = await sut.GetOpenIssuesAsync(
            GitHubComBaseUrl,
            Slug,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        IReadOnlyList<OpenIssue> issues = result.ShouldBeOfType<Result<IReadOnlyList<OpenIssue>>.Success>().Value;
        issues.Select(issue => issue.Number).ShouldBe([867, 866, 865]);
    }

    [Fact]
    public async Task WhenReading_AsksForTheNewestHundredOpenIssuesAndTheRateLimit()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, CapturedOpenIssues);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetOpenIssuesAsync(GitHubComBaseUrl, Slug, token: "ghp_token", CancellationToken.None);

        // Assert
        string requestBody = handler.LastRequestBody.ShouldNotBeNull();
        requestBody.ShouldContain("issues(first: 100, states: OPEN, orderBy: {field: CREATED_AT, direction: DESC})");
        requestBody.ShouldContain("rateLimit { cost remaining limit resetAt }");
    }

    [Fact]
    public async Task WhenGitHubAnswersWithAGraphQlErrorEnvelope_FailsTheRead()
    {
        // Arrange
        // Derived: the captured 999999 not-found envelope's error shape around a null repository.
        string errorEnvelope = """
            {"data":{"repository":null},"errors":[{"type":"NOT_FOUND","path":["repository"],"message":"Could not resolve to a Repository with the name 'karirafn/foundry'."}]}
            """;
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, errorEnvelope));

        // Act
        Result<IReadOnlyList<OpenIssue>> result = await sut.GetOpenIssuesAsync(
            GitHubComBaseUrl,
            Slug,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<OpenIssue>>.Failure>().Error;
        error.Message.ShouldContain("Could not resolve to a Repository");
    }

    [Fact]
    public async Task WhenGitHubAnswersWithANonSuccessStatus_FailsTheRead()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.BadGateway, string.Empty));

        // Act
        Result<IReadOnlyList<OpenIssue>> result = await sut.GetOpenIssuesAsync(
            GitHubComBaseUrl,
            Slug,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<OpenIssue>>.Failure>().Error;
        error.Message.ShouldContain("502");
    }

    [Fact]
    public async Task WhenReading_RecordsTheGraphQlHeadroom()
    {
        // Arrange
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, CapturedOpenIssues), budget);

        // Act
        await sut.GetOpenIssuesAsync(GitHubComBaseUrl, Slug, token: "ghp_token", CancellationToken.None);

        // Assert
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull();
        reading.Remaining.ShouldBe(4986);
    }
}
