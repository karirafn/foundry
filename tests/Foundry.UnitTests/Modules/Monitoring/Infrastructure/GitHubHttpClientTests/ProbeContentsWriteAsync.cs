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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ProbeContentsWriteAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenProbing_PostBodyContainsFoundryProbeRefName()
    {
        // Arrange
        string json = """{ "message": "Object does not exist" }""";
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        string requestBody = handler.LastRequestBody.ShouldNotBeNull();
        requestBody.ShouldContain("foundry-probe-");
    }

    [Fact]
    public async Task WhenProbing_PostsToGitRefsEndpoint()
    {
        // Arrange
        string json = """{ "message": "Object does not exist" }""";
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/git/refs");
        request.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task WhenGitHubReturns403_ReturnsMissingContents()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
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
    }

    [Fact]
    public async Task WhenProbing_PostBodyContainsAllZerosSha()
    {
        // Arrange - 422 is the expected non-destructive success path
        string json = """{ "message": "Object does not exist" }""";
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert - the all-zeros SHA guarantees the ref can never be created (non-destructive by construction)
        handler.LastRequest.ShouldNotBeNull();
        string requestBody = handler.LastRequestBody.ShouldNotBeNull();
        requestBody.ShouldContain("0000000000000000000000000000000000000000");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsInvalidBaseUrlFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri nonHttpsUrl = new("http://api.github.com");

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
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

    [Fact]
    public async Task WhenGitHubReturns500_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
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
    public async Task WhenGitHubReturns404_ReturnsGranted()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
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
    public async Task WhenGitHubReturns422_ReturnsGranted()
    {
        // Arrange
        string json = """{ "message": "Object does not exist" }""";
        FakeHandler handler = new(HttpStatusCode.UnprocessableEntity, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
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
    public async Task WhenGitHubReturns401_ReturnsFailure()
    {
        // Arrange — 401 means the token expired or was revoked mid-probe; the result is
        // indeterminate, so the probe must fail closed rather than returning Granted.
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitRemainingZero_ReturnsRateLimitExhaustedFailure()
    {
        // Arrange — X-RateLimit-Remaining: 0 means the primary rate limit is exhausted;
        // this must not be misclassified as a missing permission.
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<WritePermissionProbeResult>.Failure failure = result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRetryAfterHeader_ReturnsRateLimitExhaustedFailure()
    {
        // Arrange — Retry-After header indicates a secondary rate limit (abuse detection);
        // X-RateLimit-Remaining may be absent in this case.
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["Retry-After"] = "60";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<WritePermissionProbeResult>.Failure failure = result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitRemainingNonZero_ReturnsMissingPermission()
    {
        // Arrange — headroom remaining means the 403 is a genuine permission denial, not a rate limit.
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "5";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert — genuine permission denial; existing behavior must be preserved.
        result.IsSuccess.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Success success =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>();
        success.Value.ShouldBeOfType<WritePermissionProbeResult.Missing>();
    }

    [Fact]
    public async Task WhenGitHubReturns403WithNoRateLimitHeaders_ReturnsMissingPermission()
    {
        // Arrange — no rate-limit headers → fail-closed: treat as a genuine permission denial.
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Success success =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>();
        success.Value.ShouldBeOfType<WritePermissionProbeResult.Missing>();
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRetryAfterZero_ReturnsMissingPermission()
    {
        // Arrange — Retry-After: 0 is not a valid positive-integer rate-limit signal;
        // a real GitHub secondary-rate-limit always sends a positive number of seconds.
        // An intermediary or CDN may inject Retry-After: 0 on a genuine permission 403.
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["Retry-After"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Success success =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>();
        success.Value.ShouldBeOfType<WritePermissionProbeResult.Missing>();
    }

    [Fact]
    public async Task WhenGitHubReturns403WithNonNumericRetryAfter_ReturnsMissingPermission()
    {
        // Arrange — a non-numeric Retry-After (e.g. injected by a CDN) is not a GitHub
        // secondary-rate-limit signal; the 403 must be treated as a genuine permission denial.
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["Retry-After"] = "soon";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeContentsWriteAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<WritePermissionProbeResult>.Success success =
            result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>();
        success.Value.ShouldBeOfType<WritePermissionProbeResult.Missing>();
    }
}
