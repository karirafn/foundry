using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class CreateBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("owner/repo")).Value;

    [Fact]
    public async Task WhenBranchCreatedSuccessfully_ReturnsTrue()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string createJson = """{ "ref": "refs/heads/feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Created, createJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
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
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string conflictJson = """{ "message": "Reference already exists" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.UnprocessableEntity, conflictJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGettingRefFails_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenCreatingRefFails_ReturnsFailure()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<bool> result = await sut.CreateBranchAsync(
            invalidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCreatingBranch_UsesCorrectGetRefEndpoint()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string createJson = """{ "ref": "refs/heads/feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Created, createJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        handler.Requests.Count.ShouldBe(2);
        HttpRequestMessage getRefRequest = handler.Requests[0];
        getRefRequest.RequestUri.ShouldNotBeNull();
        getRefRequest.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/git/refs/heads/main");
    }

    [Fact]
    public async Task WhenCreatingBranch_UsesCorrectCreateRefEndpoint()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string createJson = """{ "ref": "refs/heads/feat/my-branch" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Created, createJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.CreateBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        handler.Requests.Count.ShouldBe(2);
        HttpRequestMessage createRefRequest = handler.Requests[1];
        createRefRequest.RequestUri.ShouldNotBeNull();
        createRefRequest.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/git/refs");
    }
}
