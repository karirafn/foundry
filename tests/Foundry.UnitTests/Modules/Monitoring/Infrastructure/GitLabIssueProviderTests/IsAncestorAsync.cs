using System.Net;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
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

public sealed class IsAncestorAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";
    private const string AncestorSha = "1111111111111111111111111111111111111111";
    private const string OtherSha = "2222222222222222222222222222222222222222";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(HttpMessageHandler handler)
    {
        GitLabHttpClient gitLabHttpClient = new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), ValidToken, ValidBaseUrl);
    }

    private static Task<Result<bool>> CompareAsync(GitLabIssueProvider sut) =>
        sut.IsAncestorAsync(ValidSlug, "feat/42-add-retry-run-7", "feat/42-add-retry", CancellationToken.None);

    [Theory]
    [InlineData(AncestorSha, true)]
    [InlineData(OtherSha, false)]
    public async Task WhenBothRefsExist_AnswersWhetherTheMergeBaseIsTheAncestor(string mergeBaseSha, bool expected)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, $$"""{ "id": "{{AncestorSha}}" }"""),
            (HttpStatusCode.OK, $$"""{ "id": "{{mergeBaseSha}}" }"""),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        result.ValueOrThrow().ShouldBe(expected);
    }

    [Fact]
    public async Task WhenCompared_ResolvesTheAncestorThenAsksForTheMergeBaseOfBothRefs()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, $$"""{ "id": "{{AncestorSha}}" }"""),
            (HttpStatusCode.OK, $$"""{ "id": "{{AncestorSha}}" }"""),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        await CompareAsync(sut);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => handler.Requests[0].RequestUri!.AbsolutePath.ShouldBe(
                "/api/v4/projects/group%2Fproject/repository/commits/feat%2F42-add-retry-run-7"),
            () => handler.Requests[1].RequestUri!.PathAndQuery.ShouldBe(
                "/api/v4/projects/group%2Fproject/repository/merge_base" +
                "?refs%5B%5D=feat%2F42-add-retry-run-7&refs%5B%5D=feat%2F42-add-retry"));
    }

    [Fact]
    public async Task WhenTheAncestorIsMissing_FailsNotFound()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NotFound, """{ "message": "404 Commit Not Found" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        Error error = result.ErrorOrThrow();
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(RunPublishingErrors.RefNotFoundCode),
            () => error.Kind.ShouldBe(ErrorKind.NotFound),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenTheBranchIsMissing_FailsNotFound(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, $$"""{ "id": "{{AncestorSha}}" }"""),
            (statusCode, """{ "message": "Could not find ref 'feat/42-add-retry'" }"""),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.RefNotFoundCode);
    }

    [Fact]
    public async Task WhenTheProviderFailsWithAServerError_FailsTransient()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.InternalServerError, "{}")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<bool> result = await CompareAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }
}
