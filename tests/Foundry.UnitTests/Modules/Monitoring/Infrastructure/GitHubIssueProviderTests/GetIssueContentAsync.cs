using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class GetIssueContentAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(DelegatingHandler handler)
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

    private const string IssueJson = """
        { "number": 7, "title": "Add retry", "body": "Retry webhook delivery.", "user": { "login": "alice" } }
        """;

    private const string CommentsJson = """
        [
          { "id": 11, "body": "Which webhooks?", "created_at": "2026-06-01T10:00:00Z", "user": { "login": "bob", "type": "User" } },
          { "id": 12, "body": "All of them.", "created_at": "2026-06-01T11:00:00Z", "user": { "login": "alice", "type": "User" } }
        ]
        """;

    [Fact]
    public async Task WhenTheIssueHasComments_ReturnsTitleBodyAndEveryCommentOldestFirst()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, IssueJson), (HttpStatusCode.OK, CommentsJson)]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        ProviderIssueContent content = result.ValueOrThrow();
        content.ShouldSatisfyAllConditions(
            () => content.Title.ShouldBe("Add retry"),
            () => content.Body.ShouldBe("Retry webhook delivery."),
            () => content.Comments.Select(comment => comment.Id).ShouldBe(
                [ProviderCommentId.Create(11).ValueOrThrow(), ProviderCommentId.Create(12).ValueOrThrow()]),
            () => content.Comments.Select(comment => comment.AuthorLogin).ShouldBe(["bob", "alice"]),
            () => content.Comments[0].Body.ShouldBe("Which webhooks?"),
            () => content.Comments[0].CreatedAt.ShouldBe(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task WhenTheIssueIsRead_RequestsTheIssueThenItsComments()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, IssueJson), (HttpStatusCode.OK, "[]")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        handler.Requests.Select(request => request.RequestUri!.AbsolutePath).ShouldBe(
            ["/repos/owner/repo/issues/7", "/repos/owner/repo/issues/7/comments"]);
    }

    [Fact]
    public async Task WhenTheIssueHasNoBody_ReturnsAnEmptyBody()
    {
        // Arrange
        SequentialFakeHandler handler = new([
            (HttpStatusCode.OK, """{ "number": 7, "title": "Add retry", "body": null }"""),
            (HttpStatusCode.OK, "[]")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheIssueReadFails_FailsWithoutReadingComments()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NotFound, """{ "message": "Not Found" }""")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result<ProviderIssueContent>.Failure>(),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Fact]
    public async Task WhenTheCommentsReadFails_Fails()
    {
        // Arrange
        SequentialFakeHandler handler = new([
            (HttpStatusCode.OK, IssueJson),
            (HttpStatusCode.InternalServerError, "{}")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<ProviderIssueContent>.Failure>();
    }
}
