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
using Foundry.Modules.Monitoring.Features.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class RateLimitHandling
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task When429TooManyRequests_ReturnsRateLimitExhaustedError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.TooManyRequests, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.RateLimitExhausted");
    }

    [Fact]
    public async Task When403WithRateLimitRemainingZero_ReturnsRateLimitExhaustedError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders.Add("RateLimit-Remaining", "0");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.RateLimitExhausted");
    }

    [Fact]
    public async Task When403WithRateLimitRemainingNonZero_ReturnsUnexpectedStatusCodeError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders.Add("RateLimit-Remaining", "10");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.UnexpectedStatusCode");
        failure.Error.Message.ShouldContain("403");
    }

    [Fact]
    public async Task When403WithNoRateLimitHeader_ReturnsUnexpectedStatusCodeError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.UnexpectedStatusCode");
        failure.Error.Message.ShouldContain("403");
    }
}
