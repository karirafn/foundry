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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetBranchCommitSummaryAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient CreateSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

    [Fact]
    public async Task WhenBranchHasCommits_ReturnsCommitCountAndLatestSha()
    {
        // Arrange — GitLab compare returns commits[] with the branch-ahead commits
        string json = """
            {
                "commits": [
                    { "id": "aaa111", "short_id": "aaa111", "title": "first", "message": "", "author_name": "", "authored_date": "2026-01-01T00:00:00Z", "committer_name": "", "committed_date": "2026-01-01T00:00:00Z" },
                    { "id": "bbb222", "short_id": "bbb222", "title": "second", "message": "", "author_name": "", "authored_date": "2026-01-01T00:00:00Z", "committer_name": "", "committed_date": "2026-01-01T00:00:00Z" },
                    { "id": "ccc333", "short_id": "ccc333", "title": "third", "message": "", "author_name": "", "authored_date": "2026-01-01T00:00:00Z", "committer_name": "", "committed_date": "2026-01-01T00:00:00Z" }
                ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.CommitCount.ShouldBe(3),
            () => success.Value.LatestSha.ShouldBe("ccc333"));
    }

    [Fact]
    public async Task WhenBranchHasNoCommits_ReturnsZeroCountAndNullSha()
    {
        // Arrange
        string json = """{ "commits": [] }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Success success = result.ShouldBeOfType<Result<BranchCommitSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.CommitCount.ShouldBe(0),
            () => success.Value.LatestSha.ShouldBeNull());
    }

    [Fact]
    public async Task WhenGitLabReturnsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccess_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<BranchCommitSummary> result = await sut.GetBranchCommitSummaryAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<BranchCommitSummary>.Failure failure = result.ShouldBeOfType<Result<BranchCommitSummary>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }
}
