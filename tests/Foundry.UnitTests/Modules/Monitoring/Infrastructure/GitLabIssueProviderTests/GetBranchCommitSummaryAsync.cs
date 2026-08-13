using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class GetBranchCommitSummaryAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        return new GitLabIssueProvider(gitLabHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchHasCommitsAheadOfDefault_ReturnsNonZeroCommitCount()
    {
        // Arrange
        string projectJson = """{ "default_branch": "main" }""";
        string compareJson = """
            {
                "commits": [
                    { "id": "aaa111", "short_id": "aaa111", "title": "first", "message": "", "author_name": "", "authored_date": "2026-01-01T00:00:00Z", "committer_name": "", "committed_date": "2026-01-01T00:00:00Z" },
                    { "id": "bbb222", "short_id": "bbb222", "title": "second", "message": "", "author_name": "", "authored_date": "2026-01-01T00:00:00Z", "committer_name": "", "committed_date": "2026-01-01T00:00:00Z" }
                ]
            }
            """;
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.OK, compareJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.CommitCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenBranchHasNoCommitsAheadOfDefault_ReturnsZeroCommitCount()
    {
        // Arrange
        string projectJson = """{ "default_branch": "main" }""";
        string compareJson = """{ "commits": [] }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.OK, compareJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

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
    public async Task WhenGetDefaultBranchFails_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCompareApiFails_ReturnsFailure()
    {
        // Arrange
        string projectJson = """{ "default_branch": "main" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

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
        string projectJson = """{ "default_branch": "main" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.NotFound, string.Empty),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

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
