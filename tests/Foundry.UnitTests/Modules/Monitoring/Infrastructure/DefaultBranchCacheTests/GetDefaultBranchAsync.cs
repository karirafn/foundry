using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.DefaultBranchCacheTests;

public sealed class GetDefaultBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static IMemoryCache CreateMemoryCache()
    {
        return new MemoryCache(Options.Create(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task WhenCalledTwiceForSameRepo_IssuesOnlyOneHttpRequest()
    {
        // Arrange
        string json = """{ "default_branch": "main" }""";
        CountingFakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        using IMemoryCache memoryCache = CreateMemoryCache();
        DefaultBranchCache cache = new(memoryCache);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, cache);

        // Act
        Result<string> first = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);
        Result<string> second = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenCalledForDifferentSlugs_IssuesSeparateHttpRequests()
    {
        // Arrange
        string json = """{ "default_branch": "main" }""";
        CountingFakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        using IMemoryCache memoryCache = CreateMemoryCache();
        DefaultBranchCache cache = new(memoryCache);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, cache);
        RepositorySlug otherSlug = RepositorySlug.Create("owner/other-repo").ValueOrThrow();

        // Act
        Result<string> first = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);
        Result<string> second = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, otherSlug, "ghp_token", CancellationToken.None);

        // Assert
        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenCalledForDifferentHosts_IssuesSeparateHttpRequests()
    {
        // Arrange
        string json = """{ "default_branch": "main" }""";
        CountingFakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        using IMemoryCache memoryCache = CreateMemoryCache();
        DefaultBranchCache cache = new(memoryCache);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, cache);
        Uri otherBaseUrl = new("https://github.example.com");

        // Act
        Result<string> first = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);
        Result<string> second = await sut.GetDefaultBranchAsync(
            otherBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task WhenSecondCallHitsCachedValue_ReturnsSameDefaultBranch()
    {
        // Arrange
        string json = """{ "default_branch": "main" }""";
        CountingFakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        using IMemoryCache memoryCache = CreateMemoryCache();
        DefaultBranchCache cache = new(memoryCache);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, cache);

        // Act
        await sut.GetDefaultBranchAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);
        Result<string> result = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBe("main");
    }
}
