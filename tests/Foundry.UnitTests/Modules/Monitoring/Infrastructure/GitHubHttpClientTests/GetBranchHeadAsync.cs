using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetBranchHeadAsync
{
    // docs.github.com/rest/git/refs "Get a reference" example response.
    private const string DocsReference = """
        {"ref":"refs/heads/featureA","node_id":"MDM6UmVmcmVmcy9oZWFkcy9mZWF0dXJlQQ==","url":"https://api.github.com/repos/octocat/Hello-World/git/refs/heads/featureA","object":{"type":"commit","sha":"aa218f56b14c9653891f9e74264a383fa43fefbd","url":"https://api.github.com/repos/octocat/Hello-World/git/commits/aa218f56b14c9653891f9e74264a383fa43fefbd"}}
        """;

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenTheBranchExists_ReturnsItsHeadSha()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, DocsReference));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "featureA", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Success>().Value
            .ShouldBe(new BranchHead.Present("aa218f56b14c9653891f9e74264a383fa43fefbd"));
    }

    [Fact]
    public async Task WhenReading_GetsTheSingularRefOfTheBranchKeepingItsSlashes()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsReference);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetBranchHeadAsync(ValidBaseUrl, Slug, "feat/42-add-retry", "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.AllRequests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.Method.ShouldBe(HttpMethod.Get),
            () => request.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/git/ref/heads/feat/42-add-retry"));
    }

    [Fact]
    public async Task WhenTheRefDoesNotExist_ReturnsAbsent()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "feat/42-add-retry", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Success>().Value.ShouldBeOfType<BranchHead.Absent>();
    }

    [Fact]
    public async Task WhenGitHubAnswersWithAServerError_Fails()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.InternalServerError, string.Empty));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "feat/42-add-retry", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Failure>().Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenTheRefHasNoSha_Fails()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, """{"ref":"refs/heads/featureA"}"""));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "featureA", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Failure>();
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutARequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsReference);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            new Uri("http://api.github.com"), Slug, "featureA", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Failure>().Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
        handler.AllRequests.ShouldBeEmpty();
    }
}
