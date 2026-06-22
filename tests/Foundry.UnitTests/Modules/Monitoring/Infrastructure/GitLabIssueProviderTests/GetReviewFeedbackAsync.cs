using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

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
        GitLabHttpClient gitLabHttpClient = new(httpClient);
        return new GitLabIssueProvider(gitLabHttpClient, ValidToken, ValidBaseUrl);
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
                    "updated_at": "2026-06-01T00:00:00Z",
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
                    "updated_at": "2025-12-31T23:59:59Z",
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
}
