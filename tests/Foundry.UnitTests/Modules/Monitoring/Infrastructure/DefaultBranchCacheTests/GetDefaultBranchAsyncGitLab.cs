using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.DefaultBranchCacheTests;

public sealed class GetDefaultBranchAsyncGitLab
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static IMemoryCache CreateMemoryCache()
    {
        return new MemoryCache(Options.Create(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task WhenCalledTwiceForSameRepo_IssuesOnlyOneHttpRequest()
    {
        // Arrange
        string json = """{ "id": 1, "default_branch": "main" }""";
        CountingFakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        using IMemoryCache memoryCache = CreateMemoryCache();
        DefaultBranchCache cache = new(memoryCache);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, cache, new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<string> first = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);
        Result<string> second = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

        // Assert
        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenCalledForDifferentSlugs_IssuesSeparateHttpRequests()
    {
        // Arrange
        string json = """{ "id": 1, "default_branch": "main" }""";
        CountingFakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        using IMemoryCache memoryCache = CreateMemoryCache();
        DefaultBranchCache cache = new(memoryCache);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, cache, new InMemoryProviderRateBudget(), TimeProvider.System);
        RepositorySlug otherSlug = RepositorySlug.Create("group/other-project").ValueOrThrow();

        // Act
        Result<string> first = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);
        Result<string> second = await sut.GetDefaultBranchAsync(
            ValidBaseUrl, otherSlug, "glpat_token", CancellationToken.None);

        // Assert
        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(2);
    }
}
