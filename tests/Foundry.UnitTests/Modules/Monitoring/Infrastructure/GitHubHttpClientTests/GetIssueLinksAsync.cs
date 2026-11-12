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

public sealed class GetIssueLinksAsync
{
    private static readonly Uri GitHubComBaseUrl = new("https://api.github.com");

    // Captured 2026-11-12: gh api -H "X-GitHub-Api-Version: 2026-03-10"
    // "repos/karirafn/foundry/issues/848/dependencies/blocked_by?per_page=100", fields read only,
    // titles cut to 60 characters.
    private const string CapturedBlockedBy = """
        [{"number":847,"state":"closed","title":"feat(workers): each question a run asks carries one recommen","repository":{"full_name":"karirafn/foundry"}},
         {"number":845,"state":"closed","title":"feat(workers): triage reads the code and decision records be","repository":{"full_name":"karirafn/foundry"}},
         {"number":865,"state":"open","title":"fix(monitoring): a long comment reaches the run whole, and t","repository":{"full_name":"karirafn/foundry"}}]
        """;

    // Captured 2026-11-12 from repos/karirafn/foundry/issues/865/dependencies/blocking?per_page=100, same trimming.
    private const string CapturedBlocking = """
        [{"number":848,"state":"open","title":"feat(workers): runs receive the issues their issue reference","repository":{"full_name":"karirafn/foundry"}}]
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

    private static FakeHandler CapturedHandler()
    {
        return new FakeHandler(HttpStatusCode.OK, "[]")
            .WithRoute("/dependencies/blocking", HttpStatusCode.OK, CapturedBlocking)
            .WithRoute("/dependencies/blocked_by", HttpStatusCode.OK, CapturedBlockedBy);
    }

    [Fact]
    public async Task WhenTheIssueHasBlockersAndBlockedIssues_ReturnsTheBlockersThenTheBlockedIssuesOfAnyState()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(CapturedHandler());

        // Act
        Result<IReadOnlyList<IssueReference>> result = await sut.GetIssueLinksAsync(
            GitHubComBaseUrl,
            Slug,
            issueNumber: 848,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        IReadOnlyList<IssueReference> links = result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Success>().Value;
        links.Select(link => (link.Number, link.Relation)).ShouldBe(
        [
            (847, IssueRelation.BlockedBy),
            (845, IssueRelation.BlockedBy),
            (865, IssueRelation.BlockedBy),
            (848, IssueRelation.Blocks),
        ]);
    }

    [Fact]
    public async Task WhenAnEntryBelongsToAnotherRepository_LeavesItOut()
    {
        // Arrange
        // Derived: the captured 845 element with only repository.full_name changed to the example repository
        // docs.github.com uses, in a different case than the slug's.
        string crossRepositoryBlockedBy = """
            [{"number":845,"state":"closed","title":"feat(workers): triage reads the code and decision records be","repository":{"full_name":"octocat/Hello-World"}},
             {"number":865,"state":"open","title":"fix(monitoring): a long comment reaches the run whole, and t","repository":{"full_name":"KariRafn/Foundry"}}]
            """;
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, "[]")
            .WithRoute("/dependencies/blocked_by", HttpStatusCode.OK, crossRepositoryBlockedBy);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<IssueReference>> result = await sut.GetIssueLinksAsync(
            GitHubComBaseUrl,
            Slug,
            issueNumber: 848,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        IReadOnlyList<IssueReference> links = result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Success>().Value;
        links.Select(link => link.Number).ShouldBe([865]);
    }

    [Theory]
    [InlineData("/dependencies/blocked_by")]
    [InlineData("/dependencies/blocking")]
    public async Task WhenEitherCallIsNotSuccessful_FailsTheReadWithTheStatus(string failingPath)
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, "[]")
            .WithRoute(failingPath, HttpStatusCode.InternalServerError, string.Empty);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<IssueReference>> result = await sut.GetIssueLinksAsync(
            GitHubComBaseUrl,
            Slug,
            issueNumber: 848,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Failure>().Error;
        error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutCallingGitHub()
    {
        // Arrange
        FakeHandler handler = CapturedHandler();
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<IssueReference>> result = await sut.GetIssueLinksAsync(
            new Uri("http://api.github.com"),
            Slug,
            issueNumber: 848,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<IReadOnlyList<IssueReference>>.Failure>().Error;
        error.Code.ShouldBe(GitHubErrors.InvalidBaseUrl.Code);
        handler.AllRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheResponseCarriesRateLimitHeaders_RecordsTheRestHeadroom()
    {
        // Arrange
        FakeHandler handler = CapturedHandler();
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "3800";
        handler.ResponseHeaders["X-RateLimit-Limit"] = "5000";
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(handler, budget);

        // Act
        await sut.GetIssueLinksAsync(GitHubComBaseUrl, Slug, issueNumber: 848, token: "ghp_token", CancellationToken.None);

        // Assert
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubRest).ShouldNotBeNull();
        reading.Remaining.ShouldBe(3800);
    }

    [Fact]
    public async Task WhenReading_RequestsBothDependencyListsAtTheMaximumPageSizeWithTheApiVersionHeader()
    {
        // Arrange
        FakeHandler handler = CapturedHandler();
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetIssueLinksAsync(GitHubComBaseUrl, Slug, issueNumber: 848, token: "ghp_token", CancellationToken.None);

        // Assert
        handler.AllRequests.Select(request => request.RequestUri!.PathAndQuery).ShouldBe(
        [
            "/repos/karirafn/foundry/issues/848/dependencies/blocked_by?per_page=100",
            "/repos/karirafn/foundry/issues/848/dependencies/blocking?per_page=100",
        ]);
        handler.AllRequests.ShouldAllBe(request => request.Headers.GetValues("X-GitHub-Api-Version").Contains("2026-03-10"));
    }
}
