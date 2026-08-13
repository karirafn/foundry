using System.Net;

using Foundry.Modules.Monitoring.Contracts;
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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class GetMergeRequestByBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenMergedPrExists_ReturnsPresenceMerged()
    {
        // Arrange
        string prJson = """
            [
              {
                "html_url": "https://github.com/owner/repo/pull/42",
                "state": "closed",
                "merged_at": "2026-06-01T10:00:00Z",
                "updated_at": "2026-06-01T10:00:00Z",
                "head": { "ref": "feat/my-branch" }
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, prJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        GitHubIssueProvider sut = new(gitHubHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.Presence.ShouldBe(MergeRequestPresence.Merged);
    }

    [Fact]
    public async Task WhenNoPrExists_ReturnsPresenceNone()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        GitHubIssueProvider sut = new(gitHubHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<MergeRequestByBranch>.Success success = result.ShouldBeOfType<Result<MergeRequestByBranch>.Success>();
        success.Value.Presence.ShouldBe(MergeRequestPresence.None);
    }

    [Fact]
    public async Task WhenApiFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        GitHubIssueProvider sut = new(gitHubHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
