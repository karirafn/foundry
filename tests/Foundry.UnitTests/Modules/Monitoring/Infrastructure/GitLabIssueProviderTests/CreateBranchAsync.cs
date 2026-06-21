using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class CreateBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("group/project")).Value;

    private static GitLabIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient);
        return new GitLabIssueProvider(gitLabHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenBranchCreatedSuccessfully_ReturnsTrue()
    {
        // Arrange
        string projectJson = """{ "default_branch": "main" }""";
        string branchJson = """{ "name": "feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.Created, branchJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

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
        string projectJson = """{ "default_branch": "main" }""";
        string conflictJson = """{ "message": "Branch already exists" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.BadRequest, conflictJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

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
    public async Task WhenGetProjectFails_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
