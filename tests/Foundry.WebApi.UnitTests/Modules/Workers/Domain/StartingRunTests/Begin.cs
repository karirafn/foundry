using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.StartingRunTests;

public sealed class Begin
{
    [Fact]
    public void WhenCalled_ReturnsStartingRunWithCorrectIssueId()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        // Act
        StartingRun run = StartingRun.Begin(issueId);

        // Assert
        run.IssueId.ShouldBe(issueId);
    }

    [Fact]
    public void WhenCalled_AssignsNewWorkerRunId()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        // Act
        StartingRun a = StartingRun.Begin(issueId);
        StartingRun b = StartingRun.Begin(issueId);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenCalled_SetsCreatedAtToUtcNow()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        StartingRun run = StartingRun.Begin(issueId);

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        run.CreatedAt.ShouldBeInRange(before, after);
    }
}
