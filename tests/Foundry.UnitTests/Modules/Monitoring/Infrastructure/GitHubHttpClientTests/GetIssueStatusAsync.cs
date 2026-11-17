using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class GetIssueStatusAsync
{
    private static readonly Uri GitHubComBaseUrl = new("https://api.github.com");

    // Shaped from the 2026-11-12 capture of the issueOrPullRequest(number: 847) query on karirafn/foundry (see
    // GetReferencedIssueAsync), reduced to the issue(number:) { state body } fields and the rateLimit block this
    // query selects.
    private const string CapturedClosed = """
        {"data":{"rateLimit":{"cost":1,"remaining":4986},"repository":{"issue":{"state":"CLOSED","body":"## Summary\n\nEvery question a worker run "}}}}
        """;

    // As above, from the 2026-11-12 capture of issueOrPullRequest(number: 865).
    private const string CapturedOpen = """
        {"data":{"rateLimit":{"cost":1,"remaining":4904},"repository":{"issue":{"state":"OPEN","body":"## Summary\n\nBoth provider clients cut ev"}}}}
        """;

    private static RepositorySlug Slug => RepositorySlug.Create("karirafn/foundry").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler, InMemoryProviderRateBudget? budget = null) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            budget ?? new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenTheIssueIsOpen_ReturnsNotClosedWithTheBody()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, CapturedOpen));

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            GitHubComBaseUrl, Slug, issueNumber: 865, token: "token", CancellationToken.None);

        // Assert
        IssueStatus status = result.ShouldBeOfType<Result<IssueStatus>.Success>().Value;
        status.ShouldBe(new IssueStatus(IsClosed: false, Body: "## Summary\n\nBoth provider clients cut ev"));
    }

    [Fact]
    public async Task WhenTheIssueIsClosed_ReturnsClosed()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, CapturedClosed));

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            GitHubComBaseUrl, Slug, issueNumber: 847, token: "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueStatus>.Success>().Value.IsClosed.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheBodyIsNull_ReturnsTheEmptyBody()
    {
        // Arrange
        string json = CapturedOpen.Replace(
            "\"## Summary\\n\\nBoth provider clients cut ev\"", "null", StringComparison.Ordinal);
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, json));

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            GitHubComBaseUrl, Slug, issueNumber: 865, token: "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueStatus>.Success>().Value.Body.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task WhenReading_AsksForTheStateTheBodyAndTheRateLimitInOneQuery()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, CapturedOpen);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetIssueStatusAsync(GitHubComBaseUrl, Slug, issueNumber: 865, token: "token", CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(1);
        handler.LastRequest.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsolutePath.ShouldBe("/graphql");
        string requestBody = handler.LastRequestBody.ShouldNotBeNull();
        requestBody.ShouldContain("rateLimit { cost remaining }");
        requestBody.ShouldContain("issue(number: $number) { state body }");
    }

    [Fact]
    public async Task WhenReading_RecordsTheGraphQlHeadroomFromTheData()
    {
        // Arrange
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, CapturedOpen), budget);

        // Act
        await sut.GetIssueStatusAsync(GitHubComBaseUrl, Slug, issueNumber: 865, token: "token", CancellationToken.None);

        // Assert
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull();
        reading.Remaining.ShouldBe(4904);
    }

    [Fact]
    public async Task WhenGitHubIsRateLimited_ReturnsTheRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            GitHubComBaseUrl, Slug, issueNumber: 865, token: "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueStatus>.Failure>().Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenTheResponseNamesNoIssue_Fails()
    {
        // Arrange
        string json = """{"data":{"rateLimit":{"cost":1,"remaining":4904},"repository":{"issue":null}}}""";
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, json));

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            GitHubComBaseUrl, Slug, issueNumber: 865, token: "token", CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutARequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, CapturedOpen);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IssueStatus> result = await sut.GetIssueStatusAsync(
            new Uri("ftp://api.github.com"), Slug, issueNumber: 865, token: "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IssueStatus>.Failure>().Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
        handler.AllRequests.ShouldBeEmpty();
    }
}
