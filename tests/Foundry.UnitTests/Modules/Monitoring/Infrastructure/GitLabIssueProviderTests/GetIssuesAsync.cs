using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class GetIssuesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        return new GitLabIssueProvider(gitLabHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenProviderReturnsIssues_ReturnsIssueListing()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 42,
                "title": "Fix the bug",
                "description": "Bug description",
                "author": { "username": "alice" },
                "web_url": "https://gitlab.com/group/project/-/issues/42",
                "labels": [ "bug", "foundry" ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabIssueProvider sut = BuildSut(handler);

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
        GitLabIssueProvider sut = BuildSut(handler);

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
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidSlug, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
