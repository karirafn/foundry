using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
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

public sealed class GetReferencedIssueAsync
{
    private static readonly Uri GitHubComBaseUrl = new("https://api.github.com");

    // Captured 2026-11-12 with the issueOrPullRequest(number: 847) query on karirafn/foundry; body trimmed to 40 characters.
    // rateLimit block from the 2026-11-27 `gh api graphql` capture on karirafn/foundry.
    private const string Captured847 = """
        {"data":{"rateLimit":{"cost":1,"remaining":4990,"limit":5000,"resetAt":"2026-11-27T10:23:06Z"},"repository":{"issueOrPullRequest":{"__typename":"Issue","body":"## Summary\n\nEvery question a worker run ","closedByPullRequestsReferences":{"nodes":[{"state":"MERGED"}]},"labels":{"nodes":[{"name":"feature"},{"name":"foundry"}]},"state":"CLOSED","title":"feat(workers): each question a run asks carries one recommendation, and a reply accepts the ones it leaves unaddressed"}}}}
        """;

    // Captured 2026-11-12 with the issueOrPullRequest(number: 865) query on karirafn/foundry; body trimmed to 40 characters.
    // rateLimit block from the 2026-11-27 `gh api graphql` capture on karirafn/foundry.
    private const string Captured865 = """
        {"data":{"rateLimit":{"cost":1,"remaining":4990,"limit":5000,"resetAt":"2026-11-27T10:23:06Z"},"repository":{"issueOrPullRequest":{"__typename":"Issue","body":"## Summary\n\nBoth provider clients cut ev","closedByPullRequestsReferences":{"nodes":[{"state":"OPEN"}]},"labels":{"nodes":[{"name":"bug"}]},"state":"OPEN","title":"fix(monitoring): a long comment reaches the run whole, and the prompt fits the clarification log instead of each comment being cut at 4,000 characters"}}}}
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

    private static Task<Result<ProviderReferencedIssue>> ReadAsync(GitHubHttpClient sut, int issueNumber = 847)
    {
        return sut.GetReferencedIssueAsync(GitHubComBaseUrl, Slug, issueNumber, token: "ghp_token", CancellationToken.None);
    }

    // Derived from the captured 865 response: only the closedByPullRequestsReferences nodes change.
    [Theory]
    [InlineData("""[{"state":"OPEN"}]""", ClosingMergeRequestState.Open)]
    [InlineData("""[{"state":"CLOSED"}]""", ClosingMergeRequestState.Closed)]
    [InlineData("""[]""", ClosingMergeRequestState.None)]
    [InlineData("""[{"state":"CLOSED"},{"state":"MERGED"}]""", ClosingMergeRequestState.Merged)]
    [InlineData("""[{"state":"CLOSED"},{"state":"OPEN"}]""", ClosingMergeRequestState.Open)]
    [InlineData("""[{"state":"OPEN"},{"state":"MERGED"}]""", ClosingMergeRequestState.Merged)]
    public async Task WhenClosingPullRequestsExist_ReportsTheStrongestState(
        string closingNodes,
        ClosingMergeRequestState expected)
    {
        // Arrange
        string body = Captured865.Replace("""[{"state":"OPEN"}]""", closingNodes, StringComparison.Ordinal);
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, body));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 865);

        // Assert
        ProviderReferencedIssue issue = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Success>().Value;
        issue.ClosingMergeRequest.ShouldBe(expected);
    }

    [Fact]
    public async Task WhenTheNumberNamesAPullRequest_FailsAsNotAnIssue()
    {
        // Arrange
        // Captured 2026-11-12 with the issueOrPullRequest(number: 868) query on karirafn/foundry.
        // rateLimit block from the 2026-11-27 `gh api graphql` capture on karirafn/foundry.
        string capturedPullRequest = """
            {"data":{"rateLimit":{"cost":1,"remaining":4990,"limit":5000,"resetAt":"2026-11-27T10:23:06Z"},"repository":{"issueOrPullRequest":{"__typename":"PullRequest"}}}}
            """;
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, capturedPullRequest));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 868);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.Code.ShouldBe(TrackerReadErrors.NotAnIssueCode);
    }

    [Fact]
    public async Task WhenGitHubAnswersWithANotFoundEnvelope_FailsTheReadAsNotFound()
    {
        // Arrange
        // Captured 2026-11-12 with the issueOrPullRequest(number: 999999) query on karirafn/foundry.
        // rateLimit block from the 2026-11-27 `gh api graphql` capture on karirafn/foundry.
        string capturedNotFound = """
            {"data":{"rateLimit":{"cost":1,"remaining":4990,"limit":5000,"resetAt":"2026-11-27T10:23:06Z"},"repository":{"issueOrPullRequest":null}},"errors":[{"type":"NOT_FOUND","path":["repository","issueOrPullRequest"],"locations":[{"line":1,"column":157}],"message":"Could not resolve to an issue or pull request with the number of 999999."}]}
            """;
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, capturedNotFound));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 999999);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(TrackerReadErrors.NotFoundCode),
            () => error.Kind.ShouldBe(ErrorKind.NotFound));
    }

    [Fact]
    public async Task WhenGitHubAnswersWithAnotherGraphQlError_FailsTheReadWithoutNotFound()
    {
        // Arrange
        string forbidden = """
            {"data":{"repository":{"issueOrPullRequest":null}},"errors":[{"type":"FORBIDDEN","path":["repository","issueOrPullRequest"],"message":"Resource not accessible by personal access token"}]}
            """;
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, forbidden));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 999999);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.Code.ShouldNotBe(TrackerReadErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenReading_AsksForTheClosingPullRequestsIncludingClosedOnesAndTheRateLimit()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, Captured847);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await ReadAsync(sut);

        // Assert
        string requestBody = handler.LastRequestBody.ShouldNotBeNull();
        requestBody.ShouldSatisfyAllConditions(
            () => requestBody.ShouldContain("closedByPullRequestsReferences(first: 25, includeClosedPrs: true)"),
            () => requestBody.ShouldContain("rateLimit { cost remaining limit resetAt }"));
    }

    [Fact]
    public async Task WhenTheNumberNamesAnOpenIssue_ReportsItOpenWithItsOpenClosingPullRequest()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, Captured865));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 865);

        // Assert
        ProviderReferencedIssue issue = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Success>().Value;
        issue.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenReading_RecordsTheGraphQlHeadroomFromTheData()
    {
        // Arrange
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, Captured847), budget);

        // Act
        await ReadAsync(sut);

        // Assert
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull();
        reading.ShouldSatisfyAllConditions(
            () => reading.Remaining.ShouldBe(4990),
            () => reading.Limit.ShouldBe(5000),
            () => reading.ResetAt.ShouldBe(new DateTimeOffset(2026, 11, 27, 10, 23, 6, TimeSpan.Zero)));
    }

    [Fact]
    public async Task WhenTheNumberNamesAClosedIssue_MapsItsTitleStateLabelsAndBody()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, Captured847));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut);

        // Assert
        ProviderReferencedIssue issue = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Success>().Value;
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldStartWith("feat(workers): each question a run asks"),
            () => issue.IsOpen.ShouldBeFalse(),
            () => issue.Labels.ShouldBe(["feature", "foundry"]),
            () => issue.Body.ShouldStartWith("## Summary"));
    }

    [Fact]
    public async Task WhenGitHubAnswersWithANotFoundEnvelope_RecordsTheGraphQlHeadroomFromTheData()
    {
        // Arrange
        // Captured 2026-11-12 with the issueOrPullRequest(number: 999999) query on karirafn/foundry.
        // rateLimit block from the 2026-11-27 `gh api graphql` capture on karirafn/foundry.
        string capturedNotFound = """
            {"data":{"rateLimit":{"cost":1,"remaining":4990,"limit":5000,"resetAt":"2026-11-27T10:23:06Z"},"repository":{"issueOrPullRequest":null}},"errors":[{"type":"NOT_FOUND","path":["repository","issueOrPullRequest"],"locations":[{"line":1,"column":157}],"message":"Could not resolve to an issue or pull request with the number of 999999."}]}
            """;
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, capturedNotFound), budget);

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 999999);

        // Assert
        result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error.Kind.ShouldBe(ErrorKind.NotFound);
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull();
        reading.ShouldSatisfyAllConditions(
            () => reading.Remaining.ShouldBe(4990),
            () => reading.Limit.ShouldBe(5000),
            () => reading.ResetAt.ShouldBe(new DateTimeOffset(2026, 11, 27, 10, 23, 6, TimeSpan.Zero)));
    }
}
