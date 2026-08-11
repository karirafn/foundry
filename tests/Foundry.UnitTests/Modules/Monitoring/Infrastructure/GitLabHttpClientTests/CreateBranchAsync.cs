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
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class CreateBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenBranchCreatedSuccessfully_ReturnsTrue()
    {
        // Arrange
        string projectJson = """{ "id": 1, "default_branch": "main" }""";
        string branchJson = """{ "name": "feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.Created, branchJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
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
        string projectJson = """{ "id": 1, "default_branch": "main" }""";
        string conflictJson = """{ "message": "Branch already exists" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.BadRequest, conflictJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGettingProjectFails_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenCreatingBranchFails_ReturnsFailure()
    {
        // Arrange
        string projectJson = """{ "id": 1, "default_branch": "main" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            invalidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCreatingBranch_GetsDefaultBranchFirst()
    {
        // Arrange
        string projectJson = """{ "id": 1, "default_branch": "main" }""";
        string branchJson = """{ "name": "feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.Created, branchJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        handler.Requests.Count.ShouldBe(2);
        HttpRequestMessage getProjectRequest = handler.Requests[0];
        getProjectRequest.RequestUri.ShouldNotBeNull();
        getProjectRequest.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject");
    }

    [Fact]
    public async Task WhenCreatingBranch_UsesCorrectCreateEndpoint()
    {
        // Arrange
        string projectJson = """{ "id": 1, "default_branch": "main" }""";
        string branchJson = """{ "name": "feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, projectJson),
            (HttpStatusCode.Created, branchJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance);

        // Act
        await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "glpat_token",
            CancellationToken.None);

        // Assert
        handler.Requests.Count.ShouldBe(2);
        HttpRequestMessage createRequest = handler.Requests[1];
        createRequest.RequestUri.ShouldNotBeNull();
        createRequest.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/repository/branches");
    }
}
