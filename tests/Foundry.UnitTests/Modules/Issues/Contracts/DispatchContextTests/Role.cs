using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.DispatchContextTests;

public sealed class Role
{
    [Fact]
    public void WhenFresh_RoleIsImplement()
    {
        // Arrange
        DispatchContext context = new DispatchContext.Fresh("feat/42-fix");

        // Act
        DispatchRole role = context.Role;

        // Assert
        role.ShouldBe(DispatchRole.Implement);
    }

    [Fact]
    public void WhenRevision_RoleIsImplement()
    {
        // Arrange
        DispatchContext context = new DispatchContext.Revision(
            "feat/42-fix",
            "https://github.com/org/repo/pull/7",
            new RevisionTrigger.RebaseConflict());

        // Act
        DispatchRole role = context.Role;

        // Assert
        role.ShouldBe(DispatchRole.Implement);
    }

    [Fact]
    public void WhenContinuation_RoleIsImplement()
    {
        // Arrange
        DispatchContext context = new DispatchContext.Continuation("feat/42-fix", "timed out");

        // Act
        DispatchRole role = context.Role;

        // Assert
        role.ShouldBe(DispatchRole.Implement);
    }
}
