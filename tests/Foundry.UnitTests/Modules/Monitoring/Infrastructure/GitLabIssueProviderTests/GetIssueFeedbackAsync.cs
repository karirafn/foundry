using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
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

public sealed class GetIssueFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat-token";
    private static readonly DateTimeOffset Since = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(FakeHandler handler)
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

    private const string ThreadJson = """
        [
          { "id": 1, "body": "before the cutoff", "system": false, "created_at": "2026-06-01T08:00:00Z", "author": { "username": "alice" } },
          { "id": 2, "body": "Foundry's question", "system": false, "created_at": "2026-06-01T10:00:00Z", "author": { "username": "alice" } },
          { "id": 3, "body": "the answer", "system": false, "created_at": "2026-06-01T11:00:00Z", "author": { "username": "alice" } },
          { "id": 4, "body": "changed the description", "system": true, "created_at": "2026-06-01T12:00:00Z", "author": { "username": "alice" } }
        ]
        """;

    [Fact]
    public async Task WhenThreadHasNotes_ReturnsThoseAfterTheCutoffThatFoundryDidNotPost()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, ThreadJson);
        GitLabIssueProvider sut = BuildSut(handler);
        PostedCommentIds registry = new([ProviderCommentId.Create(2).ValueOrThrow()]);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueFeedbackAsync(
            ValidSlug, 3, Since, registry, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Select(c => c.Body).ShouldBe(["the answer", "changed the description"]);
    }

    [Fact]
    public async Task WhenNoteIsASystemNote_KeepsItFlagged()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, ThreadJson);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueFeedbackAsync(
            ValidSlug, 3, Since, PostedCommentIds.None, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Single(c => c.Body == "changed the description").IsSystem.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenProviderFails_ReturnsTheFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, "{}");
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetIssueFeedbackAsync(
            ValidSlug, 3, Since, PostedCommentIds.None, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
