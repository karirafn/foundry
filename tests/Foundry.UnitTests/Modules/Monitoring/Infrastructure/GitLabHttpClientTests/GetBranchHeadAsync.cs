using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetBranchHeadAsync
{
    // docs.gitlab.com/api/branches "Get single repository branch" example response, reduced to the fields around the
    // one read.
    private const string DocsBranch = """
        {"name":"master","merged":false,"protected":true,"default":true,"developers_can_push":false,"developers_can_merge":false,"can_push":true,"web_url":"https://gitlab.example.com/my-group/my-project/-/tree/master","commit":{"id":"7b5c3cc8be40ee161ae89a06bba6229da1032a0c","short_id":"7b5c3cc","title":"add projects API","message":"add projects API"}}
        """;

    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenTheBranchExists_ReturnsItsHeadCommitId()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, DocsBranch));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "master", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Success>().Value
            .ShouldBe(new BranchHead.Present("7b5c3cc8be40ee161ae89a06bba6229da1032a0c"));
    }

    [Fact]
    public async Task WhenReading_GetsTheBranchWithItsNameEncoded()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsBranch);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetBranchHeadAsync(ValidBaseUrl, Slug, "feat/42-add-retry", "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.AllRequests.ShouldHaveSingleItem();
        request.ShouldSatisfyAllConditions(
            () => request.Method.ShouldBe(HttpMethod.Get),
            () => request.RequestUri!.AbsolutePath
                .ShouldBe("/api/v4/projects/group%2Fproject/repository/branches/feat%2F42-add-retry"));
    }

    [Fact]
    public async Task WhenTheBranchDoesNotExist_ReturnsAbsent()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.NotFound, """{"message":"404 Branch Not Found"}"""));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "feat/42-add-retry", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Success>().Value.ShouldBeOfType<BranchHead.Absent>();
    }

    [Fact]
    public async Task WhenGitLabAnswersWithAServerError_Fails()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.InternalServerError, string.Empty));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "feat/42-add-retry", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Failure>().Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenTheBranchHasNoCommit_Fails()
    {
        // Arrange
        GitLabHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, """{"name":"master"}"""));

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            ValidBaseUrl, Slug, "master", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Failure>();
    }

    [Fact]
    public async Task WhenTheBaseUrlIsNotHttps_FailsWithoutARequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, DocsBranch);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<BranchHead> result = await sut.GetBranchHeadAsync(
            new Uri("http://gitlab.com/api/v4"), Slug, "master", "token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<BranchHead>.Failure>().Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
        handler.AllRequests.ShouldBeEmpty();
    }
}
