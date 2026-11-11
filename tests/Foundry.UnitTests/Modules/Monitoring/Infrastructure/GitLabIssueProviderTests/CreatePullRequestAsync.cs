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

public sealed class CreatePullRequestAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";
    private const string ProjectJson = """{ "id": 5, "default_branch": "main" }""";
    private const string CreatedJson = """{ "iid": 12, "web_url": "https://gitlab.com/group/project/-/merge_requests/12" }""";

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

    private static Task<Result<string>> CreateAsync(GitLabIssueProvider sut, string title = "feat: add retry") =>
        sut.CreatePullRequestAsync(ValidSlug, "feat/42-add-retry", title, "Body\n\nCloses #42", CancellationToken.None);

    [Fact]
    public async Task WhenCreated_PostsTheBranchIntoTheDefaultBranchAndReturnsTheWebUrl()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, ProjectJson), (HttpStatusCode.Created, CreatedJson)]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe("https://gitlab.com/group/project/-/merge_requests/12"),
            () => handler.Requests[1].Method.ShouldBe(HttpMethod.Post),
            () => handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests"),
            () => handler.Requests[1].Headers.GetValues("PRIVATE-TOKEN").ShouldBe([ValidToken]),
            () => handler.RequestBodies[1].ShouldBe(
                """{"source_branch":"feat/42-add-retry","target_branch":"main","title":"feat: add retry","description":"Body\n\nCloses #42","remove_source_branch":false}"""));
    }

    [Fact]
    public async Task WhenAMergeRequestAlreadyExistsForTheBranch_ReturnsTheOpenOnesUrl()
    {
        // Arrange
        string openJson = """[{ "iid": 9, "state": "opened", "web_url": "https://gitlab.com/group/project/-/merge_requests/9" }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectJson),
            (HttpStatusCode.Conflict, """{ "message": ["Another open merge request already exists for this source branch: !9"] }"""),
            (HttpStatusCode.OK, openJson),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe("https://gitlab.com/group/project/-/merge_requests/9"),
            () => handler.Requests[2].Method.ShouldBe(HttpMethod.Get),
            () => handler.Requests[2].RequestUri!.PathAndQuery.ShouldBe(
                "/api/v4/projects/group%2Fproject/merge_requests?source_branch=feat%2F42-add-retry&state=opened"));
    }

    [Fact]
    public async Task WhenAMergeRequestAlreadyExistsButNoneIsOpen_FailsPermanent()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, ProjectJson),
            (HttpStatusCode.Conflict, """{ "message": ["Another open merge request already exists"] }"""),
            (HttpStatusCode.OK, "[]"),
        ]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenProviderAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, ProjectJson), (statusCode, "{}")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task WhenProviderRejectsTheRequest_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, ProjectJson), (statusCode, """{ "message": "nope" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTitleIsBlank_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut, " ");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.ShouldBeEmpty());
    }
}
