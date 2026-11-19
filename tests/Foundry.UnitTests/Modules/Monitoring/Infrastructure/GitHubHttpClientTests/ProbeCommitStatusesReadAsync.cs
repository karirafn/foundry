using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ProbeCommitStatusesReadAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static FakeHandler CreateHandler(HttpStatusCode statusCode) =>
        new FakeHandler(HttpStatusCode.OK, """{"default_branch":"main"}""").WithRoute("/statuses", statusCode, "[]");

    private static GitHubHttpClient CreateSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static Task<Result<WritePermissionProbeResult>> ProbeAsync(GitHubHttpClient sut) =>
        sut.ProbeCommitStatusesReadAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

    [Fact]
    public async Task WhenProbing_RequestsCommitStatusesEndpoint()
    {
        // Arrange
        FakeHandler handler = CreateHandler(HttpStatusCode.OK);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        await ProbeAsync(sut);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldNotBeNull().PathAndQuery.ShouldBe("/repos/owner/repo/commits/main/statuses?per_page=1");
    }

    [Fact]
    public async Task WhenGitHubReturns200_ReturnsGranted()
    {
        // Arrange
        GitHubHttpClient sut = CreateSut(CreateHandler(HttpStatusCode.OK));

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>()
            .Value.ShouldBeOfType<WritePermissionProbeResult.Granted>();
    }

    [Fact]
    public async Task WhenGitHubReturns403_ReturnsMissingCommitStatuses()
    {
        // Arrange
        GitHubHttpClient sut = CreateSut(CreateHandler(HttpStatusCode.Forbidden));

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Success>()
            .Value.ShouldBeOfType<WritePermissionProbeResult.Missing>()
            .Permissions.ShouldBe([WritePermission.CommitStatuses]);
    }

    [Fact]
    public async Task WhenGitHubReturns403RateLimited_ReturnsRateLimitExhaustedFailure()
    {
        // Arrange
        FakeHandler handler = CreateHandler(HttpStatusCode.Forbidden);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>()
            .Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task WhenGitHubReturnsAnyOtherStatus_ReturnsFailure(HttpStatusCode statusCode)
    {
        // Arrange
        GitHubHttpClient sut = CreateSut(CreateHandler(statusCode));

        // Act
        Result<WritePermissionProbeResult> result = await ProbeAsync(sut);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>();
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsInvalidBaseUrlFailure()
    {
        // Arrange
        FakeHandler handler = CreateHandler(HttpStatusCode.OK);
        GitHubHttpClient sut = CreateSut(handler);

        // Act
        Result<WritePermissionProbeResult> result = await sut.ProbeCommitStatusesReadAsync(
            new Uri("http://api.github.com"), ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<WritePermissionProbeResult>.Failure>().Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
        handler.AllRequests.ShouldBeEmpty();
    }
}
