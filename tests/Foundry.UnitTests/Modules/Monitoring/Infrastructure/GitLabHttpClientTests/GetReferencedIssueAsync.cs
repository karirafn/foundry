using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetReferencedIssueAsync
{
    private const string IssuePathSuffix = "/issues/1";
    private const string ClosedByPathSuffix = "/issues/1/closed_by";

    // docs.gitlab.com/api/issues "Retrieve a project issue" example response, reduced to the fields read: the
    // docs example is a closed issue with iid 1 and no labels.
    private const string DocsIssue = """
        {"iid":1,"title":"Ut commodi ullam eos dolores perferendis nihil sunt.","description":"Omnis vero earum sunt corporis dolor et placeat.","state":"closed","labels":[]}
        """;

    // docs.gitlab.com/api/issues "List all merge requests that close an issue on merge" example response, reduced to
    // the fields read: one merge request with iid 6432 in state opened.
    private const string DocsClosedBy = """
        [{"iid":6432,"state":"opened"}]
        """;

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        return new GitLabHttpClient(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
    }

    private static FakeHandler HandlerWith(string issueBody, string closedByBody)
    {
        return new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(ClosedByPathSuffix, HttpStatusCode.OK, closedByBody)
            .WithRoute(IssuePathSuffix, HttpStatusCode.OK, issueBody);
    }

    private static async Task<ProviderReferencedIssue> ReadAsync(FakeHandler handler)
    {
        Result<ProviderReferencedIssue> result = await ReadResultAsync(handler);

        return result.ShouldBeOfType<Result<ProviderReferencedIssue>.Success>().Value;
    }

    private static Task<Result<ProviderReferencedIssue>> ReadResultAsync(FakeHandler handler)
    {
        return BuildSut(handler).GetReferencedIssueAsync(
            ValidBaseUrl,
            Slug,
            issueNumber: 1,
            token: "glpat_token",
            CancellationToken.None);
    }

    [Fact]
    public async Task WhenReadingTheDocsExample_MapsTheTitleBodyStateAndLabels()
    {
        // Arrange
        FakeHandler handler = HandlerWith(DocsIssue, DocsClosedBy);

        // Act
        ProviderReferencedIssue issue = await ReadAsync(handler);

        // Assert
        (issue.Title, issue.Body, issue.IsOpen, issue.Labels.Count).ShouldBe((
            "Ut commodi ullam eos dolores perferendis nihil sunt.",
            "Omnis vero earum sunt corporis dolor et placeat.",
            false,
            0));
    }

    [Theory]
    [InlineData("""[{"iid":6432,"state":"opened"}]""", ClosingMergeRequestState.Open)]
    [InlineData("""[{"iid":6432,"state":"merged"}]""", ClosingMergeRequestState.Merged)]
    public async Task WhenOneMergeRequestClosesTheIssue_MapsItsState(
        string closedBy,
        ClosingMergeRequestState expected)
    {
        // Arrange
        // Derived: the docs closed_by example element (iid 6432) with its state replaced by the one under test.
        FakeHandler handler = HandlerWith(DocsIssue, closedBy);

        // Act
        ProviderReferencedIssue issue = await ReadAsync(handler);

        // Assert
        issue.ClosingMergeRequest.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""[{"iid":1,"state":"locked"}]""", ClosingMergeRequestState.Open)]
    [InlineData("""[{"iid":1,"state":"closed"}]""", ClosingMergeRequestState.Closed)]
    [InlineData("[]", ClosingMergeRequestState.None)]
    [InlineData("""[{"iid":1,"state":"closed"},{"iid":2,"state":"opened"}]""", ClosingMergeRequestState.Open)]
    [InlineData("""[{"iid":1,"state":"opened"},{"iid":2,"state":"merged"}]""", ClosingMergeRequestState.Merged)]
    [InlineData("""[{"iid":1,"state":"merged"},{"iid":2,"state":"closed"}]""", ClosingMergeRequestState.Merged)]
    [InlineData("""[{"iid":1,"state":"closed"},{"iid":2,"state":"locked"}]""", ClosingMergeRequestState.Open)]
    public async Task WhenSeveralOrOtherMergeRequestsCloseTheIssue_TheStrongestStateWins(
        string closedBy,
        ClosingMergeRequestState expected)
    {
        // Arrange
        // Derived: elements shaped like the docs closed_by example element, with iids and states chosen to cover
        // locked (GitLab's transitional merging state, per its merge requests API), none, and each ordering.
        FakeHandler handler = HandlerWith(DocsIssue, closedBy);

        // Act
        ProviderReferencedIssue issue = await ReadAsync(handler);

        // Assert
        issue.ClosingMergeRequest.ShouldBe(expected);
    }

    [Fact]
    public async Task WhenTheIssueIsOpenWithLabelsAndNoDescription_MapsOpenLabelsAndAnEmptyBody()
    {
        // Arrange
        // Derived: the docs "Retrieve a project issue" example with state opened, two labels and a null description.
        string issueBody = """{"iid":1,"title":"Add retry","description":null,"state":"opened","labels":["bug","ready"]}""";
        FakeHandler handler = HandlerWith(issueBody, "[]");

        // Act
        ProviderReferencedIssue issue = await ReadAsync(handler);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.IsOpen.ShouldBeTrue(),
            () => issue.Body.ShouldBeEmpty(),
            () => issue.Labels.ShouldBe(["bug", "ready"]));
    }

    [Fact]
    public async Task WhenTheIssueIsConfidential_FailsTheReadAsConfidentialWithoutReadingClosedBy()
    {
        // Arrange
        // Derived: the docs "Retrieve a project issue" example with its confidential field set to true.
        string issueBody =
            """{"iid":1,"title":"Secret","description":"Hidden","state":"opened","labels":["s"],"confidential":true}""";
        FakeHandler handler = HandlerWith(issueBody, DocsClosedBy);

        // Act
        Result<ProviderReferencedIssue> result = await ReadResultAsync(handler);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.Code.ShouldBe(TrackerReadErrors.ConfidentialCode);
        handler.AllRequests.ShouldNotContain(request => request.RequestUri!.AbsolutePath.EndsWith(ClosedByPathSuffix));
    }

    [Fact]
    public async Task WhenReading_RequestsTheIssueAndItsClosedByAndNeverRelatedMergeRequests()
    {
        // Arrange
        FakeHandler handler = HandlerWith(DocsIssue, DocsClosedBy);

        // Act
        await ReadAsync(handler);

        // Assert
        handler.AllRequests.Select(request => request.RequestUri!.AbsolutePath).ShouldBe(
        [
            "/api/v4/projects/group%2Fproject/issues/1",
            "/api/v4/projects/group%2Fproject/issues/1/closed_by",
        ]);
    }

    [Fact]
    public async Task WhenTheIssueCallFails_FailsTheReadWithTheStatusWithoutReadingClosedBy()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(IssuePathSuffix, HttpStatusCode.InternalServerError, string.Empty);

        // Act
        Result<ProviderReferencedIssue> result = await ReadResultAsync(handler);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.Message.ShouldContain("500");
        handler.AllRequests.ShouldNotContain(request => request.RequestUri!.AbsolutePath.EndsWith(ClosedByPathSuffix));
    }

    [Fact]
    public async Task WhenTheClosedByCallFails_FailsTheReadWithTheStatus()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.NotFound, string.Empty)
            .WithRoute(ClosedByPathSuffix, HttpStatusCode.InternalServerError, string.Empty)
            .WithRoute(IssuePathSuffix, HttpStatusCode.OK, DocsIssue);

        // Act
        Result<ProviderReferencedIssue> result = await ReadResultAsync(handler);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutCallingGitLab()
    {
        // Arrange
        FakeHandler handler = HandlerWith(DocsIssue, DocsClosedBy);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderReferencedIssue> result = await sut.GetReferencedIssueAsync(
            new Uri("http://gitlab.com/api/v4"),
            Slug,
            issueNumber: 1,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Error error = result.ShouldBeOfType<Result<ProviderReferencedIssue>.Failure>().Error;
        error.Code.ShouldBe(GitLabErrors.InvalidBaseUrl.Code);
        handler.AllRequests.ShouldBeEmpty();
    }
}
