using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Contracts.IssueIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsIssueIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        IssueId id = IssueId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
