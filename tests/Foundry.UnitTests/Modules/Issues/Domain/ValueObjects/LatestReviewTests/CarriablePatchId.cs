using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.LatestReviewTests;

public sealed class CarriablePatchId
{
    private const string PatchId = "0123456789012345678901234567890123456789";

    [Theory]
    [InlineData(ReviewVerdictOutcome.Approved)]
    [InlineData(ReviewVerdictOutcome.ApprovedForPerson)]
    public void WhenTheReviewApproved_ReturnsItsPatchId(ReviewVerdictOutcome outcome)
    {
        // Arrange
        LatestReview latestReview = new(WorkerRunId.New(), outcome, PatchId);

        // Act
        string? carriable = latestReview.CarriablePatchId;

        // Assert
        carriable.ShouldBe(PatchId);
    }

    [Fact]
    public void WhenTheReviewRequestedChanges_ReturnsNull()
    {
        // Arrange
        LatestReview latestReview = new(WorkerRunId.New(), ReviewVerdictOutcome.ChangesRequested, PatchId);

        // Act
        string? carriable = latestReview.CarriablePatchId;

        // Assert
        carriable.ShouldBeNull();
    }

    [Fact]
    public void WhenThePatchIdIsBlank_Throws()
    {
        // Act
        Action create = () => _ = new LatestReview(WorkerRunId.New(), ReviewVerdictOutcome.Approved, " ");

        // Assert
        create.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void WhenTheOutcomeIsUndefined_Throws()
    {
        // Act
        Action create = () => _ = new LatestReview(WorkerRunId.New(), (ReviewVerdictOutcome)99, PatchId);

        // Assert
        create.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
