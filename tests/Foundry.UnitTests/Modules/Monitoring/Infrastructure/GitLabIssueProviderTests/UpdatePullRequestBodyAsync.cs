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

public sealed class UpdatePullRequestBodyAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";
    private const string MergeRequestUrl = "https://gitlab.com/group/project/-/merge_requests/12";
    private const string UpdatedJson = """{ "iid": 12, "description": "Body\n\nCloses #42" }""";

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

    private static Task<Result> UpdateAsync(GitLabIssueProvider sut, string mergeRequestUrl = MergeRequestUrl) =>
        sut.UpdatePullRequestBodyAsync(ValidSlug, mergeRequestUrl, "Body\n\nCloses #42", CancellationToken.None);

    [Fact]
    public async Task WhenAccepted_PutsOnlyTheDescriptionOfTheMergeRequest()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, UpdatedJson)]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Success>(),
            () => handler.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Put),
            () => handler.Requests[0].RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/12"),
            () => handler.Requests[0].Headers.GetValues("PRIVATE-TOKEN").ShouldBe([ValidToken]),
            () => handler.RequestBodies[0].ShouldBe("""{"description":"Body\n\nCloses #42"}"""));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenProviderRefusesTheToken_FailsRefused(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, """{ "message": "nope" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        Error error = result.ErrorOrThrow();
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(RunPublishingErrors.RefusedCode),
            () => error.Message.ShouldContain($"returned {(int)statusCode}"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenProviderAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, "{}")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.TransientCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task WhenProviderRejectsTheRequest_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, """{ "message": "nope" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheUrlNamesNoMergeRequest_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut, "https://gitlab.com/group/project/-/issues/12");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.ShouldBeEmpty());
    }
}
