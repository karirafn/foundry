using System.Net;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class CreatePullRequestAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private const string RepoJson = """{ "default_branch": "main" }""";
    private const string CreatedJson = """{ "number": 12, "html_url": "https://github.com/owner/repo/pull/12" }""";

    private const string AlreadyExistsJson = """
        {
          "message": "Validation Failed",
          "errors": [
            { "resource": "PullRequest", "code": "custom", "message": "A pull request already exists for owner:feat/42-add-retry." }
          ]
        }
        """;

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitHubIssueProvider(
            gitHubHttpClient,
            new GitHubJobLogClient(httpClient),
            new ActionableFeedbackPolicy(TimeProvider.System),
            ValidToken,
            ValidBaseUrl);
    }

    private static Task<Result<string>> CreateAsync(GitHubIssueProvider sut, string title = "feat: add retry") =>
        sut.CreatePullRequestAsync(ValidSlug, "feat/42-add-retry", title, "Body\n\nCloses #42", CancellationToken.None);

    [Fact]
    public async Task WhenCreated_PostsTheBranchIntoTheDefaultBranchAndReturnsTheWebUrl()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, RepoJson), (HttpStatusCode.Created, CreatedJson)]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe("https://github.com/owner/repo/pull/12"),
            () => handler.Requests[1].Method.ShouldBe(HttpMethod.Post),
            () => handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/pulls"),
            () => handler.Requests[1].Headers.Authorization!.Parameter.ShouldBe(ValidToken),
            () => handler.RequestBodies[1].ShouldBe(
                """{"title":"feat: add retry","head":"feat/42-add-retry","base":"main","body":"Body\n\nCloses #42"}"""));
    }

    [Fact]
    public async Task WhenAPullRequestAlreadyExistsForTheBranch_ReturnsTheOpenOnesUrl()
    {
        // Arrange
        string openJson = """[{ "html_url": "https://github.com/owner/repo/pull/9", "state": "open" }]""";
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, RepoJson),
            (HttpStatusCode.UnprocessableEntity, AlreadyExistsJson),
            (HttpStatusCode.OK, openJson),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe("https://github.com/owner/repo/pull/9"),
            () => handler.Requests[2].Method.ShouldBe(HttpMethod.Get),
            () => handler.Requests[2].RequestUri!.PathAndQuery.ShouldBe(
                "/repos/owner/repo/pulls?head=owner%3Afeat%2F42-add-retry&state=open"));
    }

    [Fact]
    public async Task WhenAPullRequestAlreadyExistsButNoneIsOpen_FailsPermanent()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, RepoJson),
            (HttpStatusCode.UnprocessableEntity, AlreadyExistsJson),
            (HttpStatusCode.OK, "[]"),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenAnotherValidationFails_FailsPermanentWithoutLookingUpAnExistingOne()
    {
        // Arrange
        string noCommitsJson = """
            { "message": "Validation Failed", "errors": [{ "resource": "PullRequest", "code": "custom", "message": "No commits between main and feat/42-add-retry" }] }
            """;
        SequentialFakeHandler handler = new(
            [(HttpStatusCode.OK, RepoJson), (HttpStatusCode.UnprocessableEntity, noCommitsJson)]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.Count.ShouldBe(2));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenProviderAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, RepoJson), (statusCode, "{}")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenForbiddenByRateLimit_FailsTransient()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, RepoJson, new Dictionary<string, string>()),
            (HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""",
                new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0" }),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task WhenProviderRejectsTheRequest_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, RepoJson), (statusCode, """{ "message": "nope" }""")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhenTitleIsBlank_FailsPermanentWithoutCallingProvider(string title)
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<string> result = await CreateAsync(sut, title);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.ShouldBeEmpty());
    }
}
