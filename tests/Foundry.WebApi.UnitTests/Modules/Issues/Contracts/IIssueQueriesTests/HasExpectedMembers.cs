using System.Reflection;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Contracts.IIssueQueriesTests;

public sealed class HasExpectedMembers
{
    [Fact]
    public void WhenInspected_HasThreeQueryMethods()
    {
        // Arrange
        Type type = typeof(IIssueQueries);

        // Act
        MethodInfo[] methods = type.GetMethods();

        // Assert
        methods.Length.ShouldBe(3);
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
}
