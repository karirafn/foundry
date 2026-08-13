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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class GetBranchCommitSummaryAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        return new GitHubIssueProvider(gitHubHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchIsAheadOfDefault_ReturnsNonZeroCommitCount()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        string compareJson = """
            {
                "ahead_by": 3,
                "behind_by": 0,
                "head_commit": { "sha": "ccc333" }
            }
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.OK, compareJson),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.CommitCount.ShouldBe(3);
    }

    [Fact]
    public async Task WhenBranchHasNoNewCommits_ReturnsZeroCommitCount()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        string compareJson = """
            {
                "ahead_by": 0,
                "behind_by": 0,
                "head_commit": null
            }
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.OK, compareJson),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.CommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenRepoApiFails_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCompareApiReturnsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.NotFound, string.Empty),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }
}
