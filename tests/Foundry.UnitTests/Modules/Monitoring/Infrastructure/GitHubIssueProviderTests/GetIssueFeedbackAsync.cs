using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
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

public sealed class GetIssueFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private static readonly DateTimeOffset Since = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(FakeHandler handler)
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

    // GitHub's `since` filters by update time, so an old comment edited after the cutoff is still returned.
    private const string ThreadJson = """
        [
          { "id": 1, "body": "edited old comment", "created_at": "2026-06-01T08:00:00Z", "user": { "login": "alice", "type": "User" } },
          { "id": 2, "body": "Foundry's question", "created_at": "2026-06-01T10:00:00Z", "user": { "login": "alice", "type": "User" } },
          { "id": 3, "body": "the answer", "created_at": "2026-06-01T11:00:00Z", "user": { "login": "alice", "type": "User" } },
          { "id": 4, "body": "a bot note", "created_at": "2026-06-01T12:00:00Z", "user": { "login": "ci[bot]", "type": "Bot" } }
        ]
        """;

    [Fact]
    public async Task WhenThreadHasComments_ReturnsThoseAfterTheCutoffThatFoundryDidNotPost()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, ThreadJson);
        GitHubIssueProvider sut = BuildSut(handler);
        PostedCommentIds registry = new([ProviderCommentId.Create(2).ValueOrThrow()]);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueFeedbackAsync(
            ValidSlug, 7, Since, registry, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Select(c => c.Body).ShouldBe(["the answer", "a bot note"]);
    }

    [Fact]
    public async Task WhenAuthorIsABot_KeepsTheCommentFlagged()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, ThreadJson);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueFeedbackAsync(
            ValidSlug, 7, Since, PostedCommentIds.None, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Single(c => c.Body == "a bot note").AuthorIsBot.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenProviderFails_ReturnsTheFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, "{}");
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueFeedbackAsync(
            ValidSlug, 7, Since, PostedCommentIds.None, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
