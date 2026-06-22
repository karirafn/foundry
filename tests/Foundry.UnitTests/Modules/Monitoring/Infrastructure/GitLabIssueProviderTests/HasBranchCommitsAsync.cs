using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class HasBranchCommitsAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient);
        return new GitLabIssueProvider(gitLabHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchHasCommitsAheadOfDefault_ReturnsTrue()
    {
        // Arrange
        string projectJson = """{ "default_branch": "main" }""";
        string compareJson = """{ "commits": [{ "id": "abc123" }] }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.OK, compareJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.HasBranchCommitsAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenBranchHasNoCommitsAheadOfDefault_ReturnsFalse()
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
        Result<bool> result = await sut.HasBranchCommitsAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeFalse();
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
        Result<bool> result = await sut.HasBranchCommitsAsync(
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
        Result<bool> result = await sut.HasBranchCommitsAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
