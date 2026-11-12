using System.Net;

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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetIssueNotesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat-token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(DelegatingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string NoteJson(long id, string username, bool system = false, string body = "A reply") =>
        $$"""
        {
          "id": {{id}},
          "body": "{{body}}",
          "system": {{(system ? "true" : "false")}},
          "created_at": "2026-06-01T10:00:00Z",
          "updated_at": "2026-06-01T10:00:00Z",
          "author": { "username": "{{username}}" }
        }
        """;

    [Fact]
    public async Task WhenIssueHasNotes_MapsIdAuthorBodyAndCreatedTime()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, $"[{NoteJson(77, "alice", body: "Please clarify")}]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueNotesAsync(
            ValidBaseUrl, ValidSlug, 3, ValidToken, CancellationToken.None);

        // Assert
        ProviderComment comment = result.ValueOrThrow().ShouldHaveSingleItem();
        comment.ShouldSatisfyAllConditions(
            () => comment.Id.ShouldBe(ProviderCommentId.Create(77).ValueOrThrow()),
            () => comment.AuthorLogin.ShouldBe("alice"),
            () => comment.Body.ShouldBe("Please clarify"),
            () => comment.CreatedAt.ShouldBe(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)),
            () => comment.AuthorIsBot.ShouldBeFalse(),
            () => comment.IsSystem.ShouldBeFalse(),
            () => comment.Origin.ShouldBe(CommentOrigin.Conversation));
    }

    [Fact]
    public async Task WhenNoteIsASystemNote_FlagsIt()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, $"[{NoteJson(77, "alice", system: true)}]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueNotesAsync(
            ValidBaseUrl, ValidSlug, 3, ValidToken, CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldHaveSingleItem().IsSystem.ShouldBeTrue();
    }

    [Theory]
    [InlineData("project_123_bot", true)]
    [InlineData("project_123_bot_3f2a9c1d", true)]
    [InlineData("group_45_bot2", true)]
    [InlineData("alice", false)]
    [InlineData("project_bot_fan", false)]
    public async Task WhenAuthorIsAnAccessTokenBot_FlagsIt(string username, bool expectedBot)
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, $"[{NoteJson(77, username)}]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueNotesAsync(
            ValidBaseUrl, ValidSlug, 3, ValidToken, CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldHaveSingleItem().AuthorIsBot.ShouldBe(expectedBot);
    }

    [Fact]
    public async Task WhenCalled_RequestsTheIssueNotesOldestFirst()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetIssueNotesAsync(ValidBaseUrl, ValidSlug, 3, ValidToken, CancellationToken.None);

        // Assert
        Uri requestUri = handler.LastRequest!.RequestUri!;
        requestUri.ShouldSatisfyAllConditions(
            () => requestUri.AbsoluteUri.ShouldStartWith("https://gitlab.com/api/v4/projects/group%2Fproject/issues/3/notes?"),
            () => requestUri.Query.ShouldContain("sort=asc"),
            () => requestUri.Query.ShouldContain("order_by=created_at"));
    }

    [Fact]
    public async Task WhenNoteBodyExceeds4000Chars_ReturnsBodyWhole()
    {
        // Arrange
        string longBody = new('x', 4100);
        FakeHandler handler = new(HttpStatusCode.OK, $"[{NoteJson(77, "alice", body: longBody)}]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueNotesAsync(
            ValidBaseUrl, ValidSlug, 3, ValidToken, CancellationToken.None);

        // Assert
        ProviderComment comment = result.ValueOrThrow().ShouldHaveSingleItem();
        comment.Body.ShouldBe(longBody);
    }

    [Fact]
    public async Task WhenProviderFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, "{}");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueNotesAsync(
            ValidBaseUrl, ValidSlug, 3, ValidToken, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
