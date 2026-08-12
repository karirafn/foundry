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
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ProbeWriteAccessAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenContentsProbeReturnsMissing_ReturnsMissingContentsWithoutCallingFurtherProbes()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.Forbidden, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeWriteAccessAsync(
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
        missing.Permission.ShouldBe(WritePermission.Contents);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenIssuesProbeReturnsMissing_ReturnsMissingIssuesWithoutCallingPullRequestsProbe()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.Forbidden, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeWriteAccessAsync(
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
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenPullRequestsProbeReturnsMissing_ReturnsMissingPullRequests()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.Forbidden, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeWriteAccessAsync(
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
        missing.Permission.ShouldBe(WritePermission.PullRequests);
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task WhenContentsProbeFailsWithTransportError_ReturnsFailureWithoutCallingFurtherProbes()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeWriteAccessAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenIssuesProbeFailsWithTransportError_ReturnsFailureWithoutCallingPullRequestsProbe()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeWriteAccessAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenPullRequestsProbeFailsWithTransportError_ReturnsFailure()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeWriteAccessAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task WhenAllGranted_CallsProbesInContentsIssuesPullRequestsOrder()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.UnprocessableEntity, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        await sut.ProbeWriteAccessAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        handler.Requests.Count.ShouldBe(3);
        handler.Requests[0].RequestUri?.AbsolutePath.ShouldBe("/repos/owner/repo/git/refs");
        handler.Requests[1].RequestUri?.AbsolutePath.ShouldBe("/repos/owner/repo/issues");
        handler.Requests[2].RequestUri?.AbsolutePath.ShouldBe("/repos/owner/repo/pulls");
    }

    [Fact]
    public async Task WhenAllProbesGranted_ReturnsGranted()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.UnprocessableEntity, string.Empty),
            (HttpStatusCode.UnprocessableEntity, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeWriteAccessAsync(
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
}
