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

public sealed class GetIssuesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenGitLabReturnsIssues_ParsesResponseCorrectly()
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
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Success>();
        IReadOnlyList<ProviderIssue> issues = success.Value;
        issues.Count.ShouldBe(1);
        ProviderIssue issue = issues[0];
        issue.ShouldSatisfyAllConditions(
            () => issue.Number.ShouldBe(42),
            () => issue.Title.ShouldBe("Fix the bug"),
            () => issue.Body.ShouldBe("Bug description"),
            () => issue.Author.ShouldBe("alice"),
            () => issue.Url.ShouldBe("https://gitlab.com/group/project/-/issues/42"),
            () => issue.Labels.ShouldBe(["bug", "foundry"]),
            () => issue.IssueKindLabel.ShouldBe("bug"));
    }

    [Fact]
    public async Task WhenCalled_UsesPrivateTokenHeader()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_mytoken", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.TryGetValues("PRIVATE-TOKEN", out IEnumerable<string>? values).ShouldBeTrue();
        values.ShouldNotBeNull();
        values.FirstOrDefault().ShouldBe("glpat_mytoken");
    }

    [Fact]
    public async Task WhenCalled_UsesUrlEncodedProjectPath()
    {
        // Arrange
        RepositorySlug nestedSlug = RepositorySlug.Create("group/subgroup/project").ValueOrThrow();
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.GetIssuesAsync(ValidBaseUrl, nestedSlug, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldContain("group%2Fsubgroup%2Fproject");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.IsAbsoluteUri.ShouldBeTrue();
        request.RequestUri.AbsolutePath.ShouldContain("projects");
        request.RequestUri.Query.ShouldContain("labels=foundry");
        request.RequestUri.Query.ShouldContain("state=opened");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            invalidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
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
        failure.Error.Message.ShouldContain("500");
    }
}
