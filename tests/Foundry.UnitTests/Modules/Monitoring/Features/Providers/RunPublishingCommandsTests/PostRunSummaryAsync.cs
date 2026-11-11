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

public sealed class PostRunSummaryAsync : RunPublishingCommandsTestBase
{
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/12";

    private Task<Result> PostAsync(MonitoredRepositoryId repositoryId) =>
        Sut.PostRunSummaryAsync(repositoryId, PullRequestUrl, "Run summary", TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenPosted_PostsOnThePullRequestThreadAndSavesTheCommentInTheRegistry()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();

        // Act
        Result result = await PostAsync(repositoryId);

        // Assert
        DbContext.ChangeTracker.Clear();
        List<PostedComment> posted = await DbContext.Set<PostedComment>()
            .ToListAsync(TestContext.Current.CancellationToken);
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => Provider.Calls.ShouldBe(
                [$"comment owner/repo {new CommentThread.PullRequest(PullRequestUrl)} Run summary"]),
            () => posted.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                comment => comment.MonitoredRepositoryId.ShouldBe(repositoryId),
                comment => comment.CommentId.ShouldBe(ProviderCommentId.Create(555).ValueOrThrow()),
                comment => comment.PostedAt.ShouldBe(Now)));
    }

    [Theory]
    [InlineData(CommentPostErrors.TransientCode, RunPublishingErrors.TransientCode)]
    [InlineData(CommentPostErrors.PermanentCode, RunPublishingErrors.PermanentCode)]
    public async Task WhenThePostFails_MapsTheFailureAndRecordsNothing(string commentCode, string expectedCode)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.Comment = Result<ProviderCommentId>.Fail(new Error(commentCode, "post failed"));

        // Act
        Result result = await PostAsync(repositoryId);

        // Assert
        Error error = result.ShouldBeOfType<Result.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(expectedCode),
            () => error.Message.ShouldBe("post failed"),
            () => DbContext.Set<PostedComment>().ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheRepositoryIsUnknown_FailsPermanentWithoutCallingTheProvider()
    {
        // Act
        Result result = await PostAsync(MonitoredRepositoryId.New());

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => Provider.Calls.ShouldBeEmpty());
    }
}
