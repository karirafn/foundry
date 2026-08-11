using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetMergeRequestByBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenEmptyArray_ReturnsPresenceNone()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.Presence.ShouldBe(MergeRequestPresence.None);
        success.Value.WebUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WhenMergedMrExists_ReturnsPresenceMerged()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 5,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/5",
                "state": "merged",
                "updated_at": "2026-06-01T10:00:00.000Z"
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Merged),
            () => success.Value.WebUrl.ShouldBe("https://gitlab.com/group/project/-/merge_requests/5"));
    }

    [Fact]
    public async Task WhenOpenMrExists_ReturnsPresenceOpen()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 7,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/7",
                "state": "opened",
                "updated_at": "2026-06-01T10:00:00.000Z"
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Open),
            () => success.Value.WebUrl.ShouldBe("https://gitlab.com/group/project/-/merge_requests/7"));
    }

    [Fact]
    public async Task WhenClosedMrExists_ReturnsPresenceClosed()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 3,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/3",
                "state": "closed",
                "updated_at": "2026-05-01T08:00:00.000Z"
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Closed),
            () => success.Value.WebUrl.ShouldBe("https://gitlab.com/group/project/-/merge_requests/3"));
    }

    [Fact]
    public async Task WhenMergedAndOpenMrExist_ReturnsMergedMr()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 10,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/10",
                "state": "opened",
                "updated_at": "2026-06-02T12:00:00.000Z"
              },
              {
                "iid": 5,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/5",
                "state": "merged",
                "updated_at": "2026-06-01T10:00:00.000Z"
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Merged),
            () => success.Value.WebUrl.ShouldBe("https://gitlab.com/group/project/-/merge_requests/5"));
    }

    [Fact]
    public async Task WhenMultipleNonMergedMrsExist_ReturnsMostRecentByUpdatedAt()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 3,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/3",
                "state": "closed",
                "updated_at": "2026-05-01T08:00:00.000Z"
              },
              {
                "iid": 7,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/7",
                "state": "opened",
                "updated_at": "2026-06-01T10:00:00.000Z"
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Open),
            () => success.Value.WebUrl.ShouldBe("https://gitlab.com/group/project/-/merge_requests/7"));
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccess_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<MergeRequestByBranch>.Failure failure = result.ShouldBeOfType<Result<MergeRequestByBranch>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.Unknown);
    }

    [Fact]
    public async Task WhenGitLabReturns404_ReturnsNotFoundKind()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<MergeRequestByBranch>.Failure failure = result.ShouldBeOfType<Result<MergeRequestByBranch>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            invalidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<MergeRequestByBranch>.Failure failure = result.ShouldBeOfType<Result<MergeRequestByBranch>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenMrStateFieldIsAbsent_ReturnsFailure()
    {
        // Arrange — JSON omits the "state" field; System.Text.Json sets State to null at runtime,
        // which would cause NullReferenceException on .ToLowerInvariant() without a null guard.
        string json = """
            [
              {
                "iid": 5,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/5",
                "updated_at": "2026-06-01T10:00:00.000Z"
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<MergeRequestByBranch>.Failure failure = result.ShouldBeOfType<Result<MergeRequestByBranch>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.MissingMergeRequestState");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointWithStateAll()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert — GitLab defaults state=all but we make it explicit for self-documentation
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests");
        request.RequestUri.Query.ShouldContain("source_branch=feat%2Fmy-branch");
        request.RequestUri.Query.ShouldContain("state=all");
    }
}
