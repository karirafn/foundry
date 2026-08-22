using System.Reflection;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.IIssueQueriesTests;

public sealed class HasExpectedMembers
{
    [Fact]
    public void WhenInspected_HasTwelveQueryMethods()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo[] methods = type.GetMethods();

        // Assert
        methods.Length.ShouldBe(12);
    }

    [Fact]
    public void WhenInspected_HasGetKnownIssueNumbersAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetKnownIssueNumbersAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetIssueSnapshotsAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetIssueSnapshotsAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetDependencyGraphAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetDependencyGraphAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetReviewIssuesAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetReviewIssuesAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetIssueSummariesAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetIssueSummariesAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetIssueSummaryAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetIssueSummaryAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetIssueDetailAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetIssueDetailAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetUntrackableIssueNumbersAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetUntrackableIssueNumbersAsync");

        // Assert
        method.ShouldNotBeNull();
    }

    [Fact]
    public void WhenInspected_HasGetDispatchCandidateIssueNumbersAsync()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo? method = type.GetMethod("GetDispatchCandidateIssueNumbersAsync");

        // Assert
        method.ShouldNotBeNull();
    }
}
