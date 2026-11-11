using System.Net;

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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetIssueCommentsAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private static readonly DateTimeOffset Since = new(2026, 6, 1, 8, 30, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(DelegatingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string CommentJson(long id, string login, string type, string body = "A reply") =>
        $$"""
        {
          "id": {{id}},
          "body": "{{body}}",
          "created_at": "2026-06-01T10:00:00Z",
          "updated_at": "2026-06-01T10:00:00Z",
          "user": { "login": "{{login}}", "type": "{{type}}" }
        }
        """;

    [Fact]
    public async Task WhenIssueHasComments_MapsIdAuthorBodyAndCreatedTime()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, $"[{CommentJson(9001, "alice", "User", "Please clarify")}]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueCommentsAsync(
            ValidBaseUrl, ValidSlug, 7, Since, ValidToken, CancellationToken.None);

        // Assert
        ProviderComment comment = result.ValueOrThrow().ShouldHaveSingleItem();
        comment.ShouldSatisfyAllConditions(
            () => comment.Id.ShouldBe(ProviderCommentId.Create(9001).ValueOrThrow()),
            () => comment.AuthorLogin.ShouldBe("alice"),
            () => comment.Body.ShouldBe("Please clarify"),
            () => comment.CreatedAt.ShouldBe(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)),
            () => comment.AuthorIsBot.ShouldBeFalse(),
            () => comment.IsSystem.ShouldBeFalse(),
            () => comment.Origin.ShouldBe(CommentOrigin.Conversation));
    }

    [Fact]
    public async Task WhenAuthorIsABot_FlagsIt()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, $"[{CommentJson(9001, "dependabot[bot]", "Bot")}]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueCommentsAsync(
            ValidBaseUrl, ValidSlug, 7, Since, ValidToken, CancellationToken.None);

        // Assert
        result.ValueOrThrow().ShouldHaveSingleItem().AuthorIsBot.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenCalled_RequestsTheIssueCommentsSinceTheCutoff()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetIssueCommentsAsync(ValidBaseUrl, ValidSlug, 7, Since, ValidToken, CancellationToken.None);

        // Assert
        Uri requestUri = handler.LastRequest!.RequestUri!;
        requestUri.ShouldSatisfyAllConditions(
            () => requestUri.AbsolutePath.ShouldBe("/repos/owner/repo/issues/7/comments"),
            () => Uri.UnescapeDataString(requestUri.Query).ShouldContain("since=2026-06-01T08:30:00Z"));
    }

    [Fact]
    public async Task WhenAPageIsFull_ReadsTheNextPage()
    {
        // Arrange
        string fullPage = "[" + string.Join(",", Enumerable.Range(1, 100).Select(i => CommentJson(i, "alice", "User"))) + "]";
        SequentialFakeHandler handler = new(
            [
                (HttpStatusCode.OK, fullPage),
                (HttpStatusCode.OK, $"[{CommentJson(101, "bob", "User")}]"),
            ]);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueCommentsAsync(
            ValidBaseUrl, ValidSlug, 7, Since, ValidToken, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Count.ShouldBe(101);
    }

    [Fact]
    public async Task WhenProviderFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, "{}");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueCommentsAsync(
            ValidBaseUrl, ValidSlug, 7, Since, ValidToken, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
