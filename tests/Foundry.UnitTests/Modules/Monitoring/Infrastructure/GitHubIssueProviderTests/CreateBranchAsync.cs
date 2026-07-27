using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class CreateBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        return new GitHubIssueProvider(gitHubHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchCreatedSuccessfully_ReturnsTrue()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string createJson = """{ "ref": "refs/heads/feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Created, createJson),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenBranchAlreadyExists_ReturnsFalse()
    {
        // Arrange
        string repoJson = """{ "default_branch": "main" }""";
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string conflictJson = """{ "message": "Reference already exists" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, repoJson),
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.UnprocessableEntity, conflictJson),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeFalse();
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
        Result<bool> result = await sut.CreateBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
