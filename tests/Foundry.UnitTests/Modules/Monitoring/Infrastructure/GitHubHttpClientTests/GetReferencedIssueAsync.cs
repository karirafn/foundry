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
    private const string Captured847 = """
        {"data":{"rateLimit":{"cost":1,"limit":5000,"remaining":4986,"resetAt":"2026-11-12T17:16:50Z"},"repository":{"issueOrPullRequest":{"__typename":"Issue","body":"## Summary\n\nEvery question a worker run ","closedByPullRequestsReferences":{"nodes":[{"state":"MERGED"}]},"labels":{"nodes":[{"name":"feature"},{"name":"foundry"}]},"state":"CLOSED","title":"feat(workers): each question a run asks carries one recommendation, and a reply accepts the ones it leaves unaddressed"}}}}
        """;

    // Captured 2026-11-12 with the issueOrPullRequest(number: 865) query on karirafn/foundry; body trimmed to 40 characters.
    private const string Captured865 = """
        {"data":{"rateLimit":{"cost":1,"limit":5000,"remaining":4904,"resetAt":"2026-11-12T16:44:39Z"},"repository":{"issueOrPullRequest":{"__typename":"Issue","body":"## Summary\n\nBoth provider clients cut ev","closedByPullRequestsReferences":{"nodes":[{"state":"OPEN"}]},"labels":{"nodes":[{"name":"bug"}]},"state":"OPEN","title":"fix(monitoring): a long comment reaches the run whole, and the prompt fits the clarification log instead of each comment being cut at 4,000 characters"}}}}
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
        string capturedPullRequest = """
            {"data":{"rateLimit":{"cost":1,"limit":5000,"remaining":4902,"resetAt":"2026-11-12T16:44:39Z"},"repository":{"issueOrPullRequest":{"__typename":"PullRequest"}}}}
            """;
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, capturedPullRequest));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 868);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.Code.ShouldBe(TrackerReadErrors.NotAnIssueCode);
    }

    [Fact]
    public async Task WhenGitHubAnswersWithANotFoundEnvelope_FailsTheReadWithoutNotAnIssue()
    {
        // Arrange
        // Captured 2026-11-12 with the issueOrPullRequest(number: 999999) query on karirafn/foundry.
        string capturedNotFound = """
            {"data":{"rateLimit":{"cost":1,"remaining":4901,"limit":5000,"resetAt":"2026-11-12T16:44:39Z"},"repository":{"issueOrPullRequest":null}},"errors":[{"type":"NOT_FOUND","path":["repository","issueOrPullRequest"],"locations":[{"line":1,"column":157}],"message":"Could not resolve to an issue or pull request with the number of 999999."}]}
            """;
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, capturedNotFound));

        // Act
        Result<ProviderReferencedIssue> result = await ReadAsync(sut, 999999);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldNotBe(TrackerReadErrors.NotAnIssueCode),
            () => error.Message.ShouldContain("Could not resolve to an issue or pull request"));
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
    public async Task WhenReading_RecordsTheGraphQlHeadroom()
    {
        // Arrange
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, Captured847), budget);

        // Act
        await ReadAsync(sut);

        // Assert
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull();
        reading.Remaining.ShouldBe(4986);
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
}
