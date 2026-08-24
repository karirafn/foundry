using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetBranchCommitSummaryAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient CreateSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

    [Fact]
    public async Task WhenBranchHasCommits_ReturnsCommitCountAndLastCommitSha()
    {
        // Arrange — real GitHub compare payload has no head_commit key; SHA comes from commits[^1]
        string json = """
            {
                "ahead_by": 3,
                "behind_by": 0,
                "commits": [
                    { "sha": "aaa111" },
                    { "sha": "bbb222" },
                    { "sha": "ccc333" }
                ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.CommitCount.ShouldBe(3),
            () => success.Value.LatestSha.ShouldBe("ccc333"));
    }

    [Fact]
    public async Task WhenBranchHasCommits_ExactlyOneHttpRequestIsIssued()
    {
        // Arrange
        string json = """
            {
                "ahead_by": 2,
                "behind_by": 0,
                "commits": [
                    { "sha": "aaa111" },
                    { "sha": "bbb222" }
                ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenAheadByIsZero_ReturnsZeroCountAndNullSha()
    {
        // Arrange
        string json = """
            {
                "ahead_by": 0,
                "behind_by": 0,
                "commits": []
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.CommitCount.ShouldBe(0),
            () => success.Value.LatestSha.ShouldBeNull());
    }

    [Fact]
    public async Task WhenGitHubReturnsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task WhenGitHubReturnsNonSuccess_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsInvalidBaseUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, string.Empty);
        GitHubHttpClient sut = CreateSut(handler);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            invalidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenBranchNameContainsSpecialCharacters_EncodesThemInUrl()
    {
        // Arrange
        string json = """{ "ahead_by": 1, "behind_by": 0, "commits": [ { "sha": "abc" } ] }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/branch?inject=true",
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        string requestUrl = request.RequestUri.ToString();
        requestUrl.ShouldNotContain("?inject=true");
        requestUrl.ShouldContain("%3F");
    }
}
