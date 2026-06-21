using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.IIssueBroadcasterTests;

public sealed class BroadcastAsync
{
    [Fact]
    public async Task WhenCalled_InvokesImplementation()
    {
        // Arrange
        IssueSummary summary = new(
            Id: Guid.NewGuid(),
            IssueNumber: 42,
            Title: "Test issue",
            State: "detected",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/42",
            FailureClassification: null,
            RepositoryEligibilityStatus: null);

        StubIssueBroadcaster sut = new();

        // Act
        await sut.BroadcastAsync(summary, CancellationToken.None);

        // Assert
        sut.BroadcastedSummary.ShouldBe(summary);
    }

    private sealed class StubIssueBroadcaster : IIssueBroadcaster
    {
        public IssueSummary? BroadcastedSummary { get; private set; }

        public Task BroadcastAsync(IssueSummary summary, CancellationToken cancellationToken)
        {
            BroadcastedSummary = summary;
            return Task.CompletedTask;
        }
    }
}
