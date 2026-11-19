using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.RunPublishingCommandsTests;

public sealed class PostInlineReviewAsync : RunPublishingCommandsTestBase
{
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/12";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static readonly InlineReviewComment[] Comments =
    [
        new("src/Retry.cs", 12, "Retries forever. (correctness)"),
        new("src/Cache.cs", 3, "Unbounded. (performance)"),
    ];

    private Task<Result<InlineReviewResult>> PostAsync(MonitoredRepositoryId repositoryId) =>
        Sut.PostInlineReviewAsync(repositoryId, PullRequestUrl, Commit, "2 findings posted inline.", Comments, TestContext.Current.CancellationToken);

    private async Task<List<PostedComment>> RegisteredAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<PostedComment>().ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenEveryCommentIsPosted_ReturnsThemAndSavesEachRegistryKey()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();

        // Act
        InlineReviewResult result = (await PostAsync(repositoryId)).ValueOrThrow();

        // Assert
        List<PostedComment> registered = await RegisteredAsync();
        result.ShouldSatisfyAllConditions(
            () => result.Posted.ShouldBe(Comments),
            () => result.Failures.ShouldBeEmpty(),
            () => Provider.Calls.ShouldBe([$"inline owner/repo {PullRequestUrl} {Commit} 2 findings posted inline. 2"]),
            () => registered.Select(c => (c.Kind, c.CommentId.Value)).ShouldBe(
                [(PostedCommentKind.ReviewNote, 900L), (PostedCommentKind.ReviewNote, 901L)], ignoreOrder: true));
    }

    [Fact]
    public async Task WhenOneReviewHoldsEveryComment_SavesItsKeyOnce()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        PostedCommentKey review = PostedCommentKey.Review(ProviderCommentId.Create(80).ValueOrThrow());
        Provider.InlineReview = Result<InlineReviewPosting>.Ok(new InlineReviewPosting(
            [.. Comments.Select(c => new PostedInlineComment(new InlineComment(c.Path, c.Line, c.Body), review))],
            []));

        // Act
        await PostAsync(repositoryId);

        // Assert
        (await RegisteredAsync()).ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            comment => comment.Kind.ShouldBe(PostedCommentKind.Review),
            comment => comment.CommentId.Value.ShouldBe(80),
            comment => comment.PostedAt.ShouldBe(Now));
    }

    [Fact]
    public async Task WhenSomeCommentsAreRejected_ReturnsOnlyThePostedOnesAndEachRejection()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.InlineReview = Result<InlineReviewPosting>.Ok(new InlineReviewPosting(
            [new PostedInlineComment(new InlineComment("src/Cache.cs", 3, Comments[1].Body), PostedCommentKey.ReviewNote(ProviderCommentId.Create(7).ValueOrThrow()))],
            [new FailedInlineComment(new InlineComment("src/Retry.cs", 12, Comments[0].Body), CommentPostErrors.Permanent("line not in diff"))]));

        // Act
        InlineReviewResult result = (await PostAsync(repositoryId)).ValueOrThrow();

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Posted.ShouldBe([Comments[1]]),
            () => result.Failures.ShouldHaveSingleItem().ShouldContain("src/Retry.cs:12"),
            () => result.Failures[0].ShouldContain("line not in diff"));
    }

    [Theory]
    [InlineData(CommentPostErrors.TransientCode, RunPublishingErrors.TransientCode)]
    [InlineData(CommentPostErrors.PermanentCode, RunPublishingErrors.PermanentCode)]
    public async Task WhenThePostFails_MapsTheFailureAndRecordsNothing(string commentCode, string expectedCode)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.InlineReview = Result<InlineReviewPosting>.Fail(new Error(commentCode, "post failed"));

        // Act
        Result<InlineReviewResult> result = await PostAsync(repositoryId);

        // Assert
        List<PostedComment> registered = await RegisteredAsync();
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(expectedCode),
            () => registered.ShouldBeEmpty());
    }
}
