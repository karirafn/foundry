using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ProbeIssuesWriteAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenProbing_PostBodyIsEmptyJsonObject()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.ProbeIssuesWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        string requestBody = handler.LastRequestBody.ShouldNotBeNull();
        requestBody.ShouldBe("{}");
    }

    [Fact]
    public async Task WhenProbing_PostsToIssuesEndpoint()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.ProbeIssuesWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/issues");
        request.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task WhenGitHubReturns422_ReturnsGranted()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeIssuesWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Success success =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>();
        success.Value.ShouldBeOfType<WritePermissionProbeResult.Granted>();
    }

    [Fact]
    public async Task WhenGitHubReturns404_ReturnsGranted()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeIssuesWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Success success =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>();
        success.Value.ShouldBeOfType<WritePermissionProbeResult.Granted>();
    }

    [Fact]
    public async Task WhenGitHubReturns403_ReturnsMissingIssues()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeIssuesWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Success success =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>();
        WritePermissionProbeResult.Missing missing =
            success.Value.ShouldBeOfType<WritePermissionProbeResult.Missing>();
        missing.Permission.ShouldBe(WritePermission.Issues);
    }

    [Fact]
    public async Task WhenGitHubReturns500_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeIssuesWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Failure failure =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsInvalidBaseUrlFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
        Uri nonHttpsUrl = new("http://api.github.com");

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeIssuesWriteAsync(
            nonHttpsUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Failure failure =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }
}
