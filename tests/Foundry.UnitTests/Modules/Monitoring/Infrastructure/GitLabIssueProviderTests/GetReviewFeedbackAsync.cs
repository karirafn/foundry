using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class GetReviewFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";
    private const string ValidMrUrl = "https://gitlab.com/group/project/-/merge_requests/1";
    private static readonly DateTimeOffset Since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabIssueProvider BuildSut(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient, new ActionableFeedbackPolicy(TimeProvider.System), ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenNoteIsAfterSince_ReturnsComment()
    {
        // Arrange
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "Review comment after cutoff",
                    "resolvable": true,
                    "resolved": false,
                    "system": false,
                    "created_at": "2026-06-01T00:00:00Z",
                    "updated_at": "2026-06-01T00:00:00Z",
                    "author": { "username": "alice" },
                    "position": null
                  }
                ]
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidMrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.Count.ShouldBe(1);
        success.Value.Comments[0].Body.ShouldBe("Review comment after cutoff");
    }

    [Fact]
    public async Task WhenNoteIsBeforeSince_ReturnsNoComments()
    {
        // Arrange
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "Old comment",
                    "resolvable": true,
                    "resolved": false,
                    "system": false,
                    "created_at": "2025-12-31T23:59:59Z",
                    "updated_at": "2025-12-31T23:59:59Z",
                    "author": { "username": "alice" },
                    "position": null
                  }
                ]
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidMrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSystemNoteIsAfterSince_PolicyExcludesItFromFeedback()
    {
        // Arrange — system:true note must be excluded by the policy even though created_at > since.
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "assigned to @alice",
                    "resolvable": false,
                    "resolved": false,
                    "system": true,
                    "created_at": "2026-06-01T00:00:00Z",
                    "updated_at": "2026-06-01T00:00:00Z",
                    "author": { "username": "gitlab" },
                    "position": null
                  }
                ]
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidMrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenFeedbackHasComments_NewestCommentAtAndOmittedCountArePopulated()
    {
        // Arrange — two notes after since, both pass policy. The newer one is returned last (chronological).
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "First comment",
                    "resolvable": false,
                    "resolved": false,
                    "system": false,
                    "created_at": "2026-06-01T10:00:00Z",
                    "updated_at": "2026-06-01T10:00:00Z",
                    "author": { "username": "alice" },
                    "position": null
                  },
                  {
                    "body": "Second comment",
                    "resolvable": false,
                    "resolved": false,
                    "system": false,
                    "created_at": "2026-06-01T11:00:00Z",
                    "updated_at": "2026-06-01T11:00:00Z",
                    "author": { "username": "bob" },
                    "position": null
                  }
                ]
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabIssueProvider sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidMrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Comments.Count.ShouldBe(2),
            () => success.Value.OmittedCommentCount.ShouldBe(0),
            () => success.Value.NewestCommentAt.ShouldBe(new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero)));
    }
}
