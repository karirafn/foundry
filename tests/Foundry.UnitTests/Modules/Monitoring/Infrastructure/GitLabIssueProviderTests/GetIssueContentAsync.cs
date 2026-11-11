using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class GetIssueContentAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat-token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(DelegatingHandler handler)
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

    private const string IssueJson = """
        { "iid": 7, "title": "Add retry", "description": "Retry webhook delivery.", "state": "opened" }
        """;

    private const string NotesJson = """
        [
          { "id": 11, "body": "Which webhooks?", "system": false, "created_at": "2026-06-01T10:00:00Z", "author": { "username": "bob" } },
          { "id": 12, "body": "added ~bug label", "system": true, "created_at": "2026-06-01T10:30:00Z", "author": { "username": "bob" } },
          { "id": 13, "body": "All of them.", "system": false, "created_at": "2026-06-01T11:00:00Z", "author": { "username": "alice" } }
        ]
        """;

    [Fact]
    public async Task WhenTheIssueHasNotes_ReturnsTitleDescriptionAndEveryPersonsNoteOldestFirst()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, IssueJson), (HttpStatusCode.OK, NotesJson)]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        ProviderIssueContent content = result.ValueOrThrow();
        content.ShouldSatisfyAllConditions(
            () => content.Title.ShouldBe("Add retry"),
            () => content.Body.ShouldBe("Retry webhook delivery."),
            () => content.Comments.Select(comment => comment.Id).ShouldBe(
                [ProviderCommentId.Create(11).ValueOrThrow(), ProviderCommentId.Create(13).ValueOrThrow()]),
            () => content.Comments.Select(comment => comment.AuthorLogin).ShouldBe(["bob", "alice"]),
            () => content.Comments[1].Body.ShouldBe("All of them."),
            () => content.Comments[1].CreatedAt.ShouldBe(new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task WhenTheIssueIsRead_RequestsTheIssueThenItsNotes()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, IssueJson), (HttpStatusCode.OK, "[]")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        handler.Requests.Select(request => request.RequestUri!.AbsolutePath).ShouldBe(
            ["/api/v4/projects/group%2Fproject/issues/7", "/api/v4/projects/group%2Fproject/issues/7/notes"]);
    }

    [Fact]
    public async Task WhenTheIssueHasNoDescription_ReturnsAnEmptyBody()
    {
        // Arrange
        SequentialFakeHandler handler = new([
            (HttpStatusCode.OK, """{ "iid": 7, "title": "Add retry", "description": null }"""),
            (HttpStatusCode.OK, "[]")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheIssueReadFails_FailsWithoutReadingNotes()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.NotFound, """{ "message": "404 Not found" }""")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result<ProviderIssueContent>.Failure>(),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Fact]
    public async Task WhenTheNotesReadFails_Fails()
    {
        // Arrange
        SequentialFakeHandler handler = new([
            (HttpStatusCode.OK, IssueJson),
            (HttpStatusCode.InternalServerError, "{}")]);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ProviderIssueContent> result = await sut.GetIssueContentAsync(ValidSlug, 7, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<ProviderIssueContent>.Failure>();
    }
}
