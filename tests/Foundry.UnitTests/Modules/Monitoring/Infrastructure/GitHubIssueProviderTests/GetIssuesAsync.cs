using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class GetIssuesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        return new GitHubIssueProvider(gitHubHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenProviderReturnsIssues_ReturnsIssueListing()
    {
        // Arrange
        string json = """
            [
              {
                "number": 42,
                "title": "Fix the bug",
                "body": "Bug description",
                "user": { "login": "octocat" },
                "html_url": "https://github.com/owner/repo/issues/42",
                "labels": [
                  { "name": "bug" },
                  { "name": "foundry" }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IssueListing>.Success success = result.ShouldBeOfType<Result<IssueListing>.Success>();
        IssueListing listing = success.Value;
        listing.Issues.Count.ShouldBe(1);
        listing.Issues[0].Number.ShouldBe(42);
    }

    [Fact]
    public async Task WhenProviderReturnsIssues_IsCompleteFlagIsTrue()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IssueListing>.Success success = result.ShouldBeOfType<Result<IssueListing>.Success>();
        success.Value.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenProviderFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
