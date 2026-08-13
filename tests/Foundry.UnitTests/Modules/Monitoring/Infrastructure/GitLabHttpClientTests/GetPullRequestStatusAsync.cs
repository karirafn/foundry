using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Foundry.Modules.Monitoring.Features.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetPullRequestStatusAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidMrUrl = "https://gitlab.com/group/project/-/merge_requests/1";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenMrIsMerged_ReturnsMergedAndClosedStatus()
    {
        // Arrange
        string json = """{ "iid": 1, "state": "merged" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsMerged.ShouldBeTrue(),
            () => success.Value.IsClosed.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenMrIsClosed_ReturnsClosedNotMerged()
    {
        // Arrange
        string json = """{ "iid": 1, "state": "closed" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsClosed.ShouldBeTrue(),
            () => success.Value.IsMerged.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenMrIsOpened_ReturnsNotClosedNotMerged()
    {
        // Arrange
        string json = """{ "iid": 1, "state": "opened" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsClosed.ShouldBeFalse(),
            () => success.Value.IsMerged.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenMrUrlCannotBeParsed_ReturnsInvalidUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://gitlab.com/group/project/-/issues/1",
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidMergeRequestUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        string json = """{ "iid": 1, "state": "opened" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/1");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            invalidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }
}
