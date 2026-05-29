using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.IssueIdTests;

public sealed class New
{
    [Fact]
    public void WhenCalledTwice_ProducesDistinctIds()
    {
        // Arrange

        // Act
        IssueId a = IssueId.New();
        IssueId b = IssueId.New();

        // Assert
        a.ShouldNotBe(b);
    }
}
