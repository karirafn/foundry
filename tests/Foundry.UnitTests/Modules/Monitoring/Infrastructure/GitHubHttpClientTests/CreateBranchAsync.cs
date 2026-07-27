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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class CreateBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

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

    [Fact]
    public async Task WhenCreateRefReturns403WithPermissionsHeader_ReturnsProviderError()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string forbiddenJson = """{ "message": "Resource not accessible by personal access token" }""";
        IReadOnlyDictionary<string, string> forbiddenHeaders = new Dictionary<string, string>
        {
            ["X-Accepted-GitHub-Permissions"] = "contents=write",
        };
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson, new Dictionary<string, string>()),
            (HttpStatusCode.Forbidden, forbiddenJson, forbiddenHeaders),
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
        failure.Error.Code.ShouldBe("GitHub.ProviderError");
        failure.Error.Message.ShouldContain("owner/repo");
        failure.Error.Message.ShouldContain("403");
        failure.Error.Message.ShouldContain("contents=write");
        failure.Error.Message.ShouldContain("organization");
    }

    [Fact]
    public async Task WhenCreateRefReturns403WithoutPermissionsHeader_ReturnsProviderErrorWithBodyMessage()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string forbiddenJson = """{ "message": "Resource not accessible by personal access token" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Forbidden, forbiddenJson),
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
        failure.Error.Code.ShouldBe("GitHub.ProviderError");
        failure.Error.Message.ShouldContain("owner/repo");
        failure.Error.Message.ShouldContain("403");
        failure.Error.Message.ShouldContain("Resource not accessible by personal access token");
        failure.Error.Message.ShouldNotContain("contents=write");
    }

    [Fact]
    public async Task WhenCreateRefReturns403WithRateLimitExhausted_ReturnsRateLimitExhausted()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string rateLimitJson = """{ "message": "API rate limit exceeded" }""";
        IReadOnlyDictionary<string, string> rateLimitHeaders = new Dictionary<string, string>
        {
            ["X-RateLimit-Remaining"] = "0",
        };
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson, new Dictionary<string, string>()),
            (HttpStatusCode.Forbidden, rateLimitJson, rateLimitHeaders),
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
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenCreateRefReturns403WithSecretInBodyMessage_ErrorMessageIsRedacted()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string tokenValue = "ghp_abcdefghijklmnopqrstuvwxyz123456";
        string forbiddenJson = $$"""{ "message": "Resource not accessible: token {{tokenValue}} is invalid" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Forbidden, forbiddenJson),
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
        failure.Error.Code.ShouldBe("GitHub.ProviderError");
        failure.Error.Message.ShouldNotContain(tokenValue);
        failure.Error.Message.ShouldContain("***");
    }

    [Fact]
    public async Task WhenCreateRefReturns403WithOverLongBodyMessage_ErrorMessageIsBounded()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string longMessage = new string('x', 600);
        string forbiddenJson = $$"""{ "message": "{{longMessage}}" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Forbidden, forbiddenJson),
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
        failure.Error.Code.ShouldBe("GitHub.ProviderError");
        failure.Error.Message.Length.ShouldBeLessThanOrEqualTo(600);
        failure.Error.Message.ShouldEndWith("...");
    }

    [Fact]
    public async Task WhenCreateRefReturnsNon403Failure_ReturnsUnchangedError()
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
        failure.Error.Code.ShouldBe("GitHub.UnexpectedStatusCode");
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenCreateRefReturns403WithUserinfoUrlInBodyMessage_UserinfoIsRedacted()
    {
        // Arrange
        string refJson = """{ "object": { "sha": "abc123" } }""";
        string forbiddenJson = """{ "message": "Access denied: https://user:supersecret@api.github.com/x" }""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, refJson),
            (HttpStatusCode.Forbidden, forbiddenJson),
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
        failure.Error.Code.ShouldBe("GitHub.ProviderError");
        failure.Error.Message.ShouldNotContain("supersecret");
        failure.Error.Message.ShouldContain("https://***@");
    }
}
