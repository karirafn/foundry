using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DependencyCycleTests;

public sealed class Create
{
    [Fact]
    public void WhenPathHasOneEntry_Fails()
    {
        // Arrange
        IReadOnlyList<int> path = [42];

        // Act
        Result<DependencyCycle> result = DependencyCycle.Create(path);

        // Assert
        result.ShouldBeOfType<Result<DependencyCycle>.Failure>()
            .Error.ShouldBe(DependencyCycleErrors.PathTooShort);
    }

    [Fact]
    public void WhenAnIssueBlocksItself_AcceptsThePathOfTwoEntries()
    {
        // Arrange
        IReadOnlyList<int> path = [42, 42];

        // Act
        Result<DependencyCycle> result = DependencyCycle.Create(path);

        // Assert
        result.ShouldBeOfType<Result<DependencyCycle>.Success>()
            .Value.Path.ShouldBe(path);
    }

    [Fact]
    public void WhenTwoIssuesBlockEachOther_AcceptsThePathOfThreeEntries()
    {
        // Arrange
        IReadOnlyList<int> path = [42, 15, 42];

        // Act
        Result<DependencyCycle> result = DependencyCycle.Create(path);

        // Assert
        result.ShouldBeOfType<Result<DependencyCycle>.Success>()
            .Value.Path.ShouldBe(path);
    }

    [Fact]
    public void WhenPathDoesNotEndWhereItStarted_Fails()
    {
        // Arrange
        IReadOnlyList<int> path = [42, 15, 31];

        // Act
        Result<DependencyCycle> result = DependencyCycle.Create(path);

        // Assert
        result.ShouldBeOfType<Result<DependencyCycle>.Failure>()
            .Error.ShouldBe(DependencyCycleErrors.PathNotClosed);
    }
}
