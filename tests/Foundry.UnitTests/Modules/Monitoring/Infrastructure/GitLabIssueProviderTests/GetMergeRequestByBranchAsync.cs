using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class GetMergeRequestByBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenMergedMrExists_ReturnsPresenceMerged()
    {
        // Arrange
        string mrJson = """
            [
              {
                "iid": 5,
                "web_url": "https://gitlab.com/group/project/-/merge_requests/5",
                "state": "merged",
                "updated_at": "2026-06-01T10:00:00.000Z"
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, mrJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        GitLabIssueProvider sut = new(gitLabHttpClient, ValidToken, ValidBaseUrl);

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
    public async Task WhenNoMrExists_ReturnsPresenceNone()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        GitLabIssueProvider sut = new(gitLabHttpClient, ValidToken, ValidBaseUrl);

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
        GitLabHttpClient gitLabHttpClient = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        GitLabIssueProvider sut = new(gitLabHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<MergeRequestByBranch> result = await sut.GetMergeRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
