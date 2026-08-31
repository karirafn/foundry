using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetMergeRequestByBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenEmptyArray_ReturnsPresenceNone()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.Presence.ShouldBe(MergeRequestPresence.None);
        success.Value.WebUrl.ShouldBeNull();
    }

    [Fact]
    public async Task WhenMergedAtIsSet_ReturnsPresenceMerged()
    {
        // Arrange
        string json = """
            [
              {
                "html_url": "https://github.com/owner/repo/pull/42",
                "state": "closed",
                "merged_at": "2026-06-01T10:00:00Z",
                "updated_at": "2026-06-01T10:00:00Z",
                "head": { "ref": "feat/my-branch" }
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Merged),
            () => success.Value.WebUrl.ShouldBe("https://github.com/owner/repo/pull/42"));
    }

    [Fact]
    public async Task WhenStateIsOpenAndMergedAtIsNull_ReturnsPresenceOpen()
    {
        // Arrange
        string json = """
            [
              {
                "html_url": "https://github.com/owner/repo/pull/42",
                "state": "open",
                "merged_at": null,
                "updated_at": "2026-06-01T10:00:00Z",
                "head": { "ref": "feat/my-branch" }
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Open),
            () => success.Value.WebUrl.ShouldBe("https://github.com/owner/repo/pull/42"));
    }

    [Fact]
    public async Task WhenStateIsClosedAndMergedAtIsNull_ReturnsPresenceClosed()
    {
        // Arrange
        string json = """
            [
              {
                "html_url": "https://github.com/owner/repo/pull/42",
                "state": "closed",
                "merged_at": null,
                "updated_at": "2026-06-01T10:00:00Z",
                "head": { "ref": "feat/my-branch" }
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Closed),
            () => success.Value.WebUrl.ShouldBe("https://github.com/owner/repo/pull/42"));
    }

    [Fact]
    public async Task WhenMergedAndOpenPrExist_ReturnsMergedPr()
    {
        // Arrange — open PR has more recent updated_at but merged PR wins
        string json = """
            [
              {
                "html_url": "https://github.com/owner/repo/pull/43",
                "state": "open",
                "merged_at": null,
                "updated_at": "2026-06-02T12:00:00Z",
                "head": { "ref": "feat/my-branch" }
              },
              {
                "html_url": "https://github.com/owner/repo/pull/42",
                "state": "closed",
                "merged_at": "2026-06-01T10:00:00Z",
                "updated_at": "2026-06-01T10:00:00Z",
                "head": { "ref": "feat/my-branch" }
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Merged),
            () => success.Value.WebUrl.ShouldBe("https://github.com/owner/repo/pull/42"));
    }

    [Fact]
    public async Task WhenMultipleNonMergedPrsExist_ReturnsMostRecentByUpdatedAt()
    {
        // Arrange
        string json = """
            [
              {
                "html_url": "https://github.com/owner/repo/pull/10",
                "state": "closed",
                "merged_at": null,
                "updated_at": "2026-05-01T08:00:00Z",
                "head": { "ref": "feat/my-branch" }
              },
              {
                "html_url": "https://github.com/owner/repo/pull/42",
                "state": "open",
                "merged_at": null,
                "updated_at": "2026-06-01T10:00:00Z",
                "head": { "ref": "feat/my-branch" }
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Presence.ShouldBe(MergeRequestPresence.Open),
            () => success.Value.WebUrl.ShouldBe("https://github.com/owner/repo/pull/42"));
    }

    [Fact]
    public async Task WhenGitHubReturnsNonSuccess_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<MergeRequestByBranch>.Failure failure = result.ShouldBeOfType<Result<MergeRequestByBranch>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.Unknown);
    }

    [Fact]
    public async Task WhenGitHubReturns404_ReturnsNotFoundKind()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
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
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            invalidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<MergeRequestByBranch>.Failure failure = result.ShouldBeOfType<Result<MergeRequestByBranch>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesStateAllAndHeadFilter()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        await sut.GetMergeRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/pulls");
        request.RequestUri.Query.ShouldContain("state=all");
        request.RequestUri.Query.ShouldContain("head=owner%3Afeat%2Fmy-branch");
    }
}
