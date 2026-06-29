using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetDependenciesAsync
{
    private const string LinksPathSuffix = "/issues/42/links";
    private const string ProjectLookupJson = """{ "id": 1, "default_branch": "main" }""";

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    // Helper: builds a FakeHandler where project lookup → 200 ProjectLookupJson
    // and links → the given status/body. Links route is registered first so it
    // takes precedence when both route keys match the same request URI.
    private static FakeHandler MakeHandler(HttpStatusCode linksStatus, string linksBody) =>
        new FakeHandler(HttpStatusCode.OK, ProjectLookupJson)
            .WithRoute(LinksPathSuffix, linksStatus, linksBody);

    [Fact]
    public async Task WhenGitLabReturnsLinks_ReturnsBlockedByIssueNumbers()
    {
        // Arrange
        string linksJson = """
            [
              {
                "iid": 10,
                "project_id": 1,
                "title": "Dependency one",
                "link_type": "is_blocked_by"
              },
              {
                "iid": 20,
                "project_id": 1,
                "title": "Dependency two",
                "link_type": "is_blocked_by"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10, 20]);
    }

    [Fact]
    public async Task WhenLinksContainNonBlockedByTypes_FiltersToBlockedByOnly()
    {
        // Arrange
        string linksJson = """
            [
              {
                "iid": 10,
                "project_id": 1,
                "title": "Blocked by",
                "link_type": "is_blocked_by"
              },
              {
                "iid": 99,
                "project_id": 1,
                "title": "Related",
                "link_type": "relates_to"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenGitLabReturns403_ReturnsEmptyList()
    {
        // Arrange
        FakeHandler handler = MakeHandler(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitLabReturns404_ReturnsEmptyList()
    {
        // Arrange
        FakeHandler handler = MakeHandler(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = MakeHandler(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange — https guard fires before any HTTP call; no routing needed
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            invalidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = MakeHandler(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert — LastRequest is the most recent request, which is the links call
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/issues/42/links");
    }

    [Fact]
    public async Task WhenBlockerIsClosed_ExcludesItFromResult()
    {
        // Arrange
        string linksJson = """
            [
              {
                "iid": 10,
                "project_id": 1,
                "title": "Closed blocker",
                "link_type": "is_blocked_by",
                "state": "closed"
              },
              {
                "iid": 20,
                "project_id": 1,
                "title": "Open blocker",
                "link_type": "is_blocked_by",
                "state": "opened"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([20]);
    }

    [Fact]
    public async Task WhenBlockerIsOpened_IncludesItInResult()
    {
        // Arrange
        string linksJson = """
            [
              {
                "iid": 30,
                "project_id": 1,
                "title": "Open blocker",
                "link_type": "is_blocked_by",
                "state": "opened"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([30]);
    }

    [Fact]
    public async Task WhenBlockerStateMissing_IncludesItInResult()
    {
        // Arrange — no project_id and no state: fail-safe keeps it
        string linksJson = """
            [
              {
                "iid": 40,
                "title": "Blocker without state",
                "link_type": "is_blocked_by"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([40]);
    }

    [Fact]
    public async Task WhenBlockerStateIsUnrecognized_IncludesItInResult()
    {
        // Arrange
        string linksJson = """
            [
              {
                "iid": 50,
                "project_id": 1,
                "title": "Blocker with unknown state",
                "link_type": "is_blocked_by",
                "state": "locked"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([50]);
    }

    [Fact]
    public async Task WhenCrossProjectBlockerLink_IsNotCountedAsBlocker()
    {
        // Arrange — iid:20 belongs to project_id:2 (cross-project); iid:10 to project_id:1 (same)
        string linksJson = """
            [
              {
                "iid": 10,
                "project_id": 1,
                "title": "Same-project blocker",
                "link_type": "is_blocked_by",
                "state": "opened"
              },
              {
                "iid": 20,
                "project_id": 2,
                "title": "Cross-project blocker",
                "link_type": "is_blocked_by",
                "state": "opened"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenSameProjectBlockerLink_IsCountedAsBlocker()
    {
        // Arrange
        string linksJson = """
            [
              {
                "iid": 10,
                "project_id": 1,
                "title": "Same-project blocker",
                "link_type": "is_blocked_by",
                "state": "opened"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenMixedSameAndCrossProjectLinks_KeepsOnlySameProjectIids()
    {
        // Arrange
        string linksJson = """
            [
              {
                "iid": 5,
                "project_id": 1,
                "title": "Same-project blocker A",
                "link_type": "is_blocked_by",
                "state": "opened"
              },
              {
                "iid": 99,
                "project_id": 2,
                "title": "Cross-project blocker",
                "link_type": "is_blocked_by",
                "state": "opened"
              },
              {
                "iid": 7,
                "project_id": 1,
                "title": "Same-project blocker B",
                "link_type": "is_blocked_by",
                "state": "opened"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([5, 7]);
    }

    [Fact]
    public async Task WhenLinkEntryMissingProjectId_IsKeptAsBlocker()
    {
        // Arrange — missing project_id is fail-safe: kept as blocker
        string linksJson = """
            [
              {
                "iid": 40,
                "title": "Blocker without project_id",
                "link_type": "is_blocked_by",
                "state": "opened"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([40]);
    }

    [Fact]
    public async Task WhenProjectIdLookupReturnsNonSuccess_ReturnsFailure()
    {
        // Arrange — project lookup fails with 500 → Result.Fail so poll retries
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenSameIidExistsInDifferentProject_CrossProjectIidNotCounted()
    {
        // Arrange — exact bug scenario: cross-project link shares same iid with a same-project issue
        string linksJson = """
            [
              {
                "iid": 42,
                "project_id": 2,
                "title": "Issue 42 in another project",
                "link_type": "is_blocked_by",
                "state": "opened"
              }
            ]
            """;

        FakeHandler handler = MakeHandler(HttpStatusCode.OK, linksJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert — cross-project link must not be counted even when iid collides
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBeEmpty();
    }
}
