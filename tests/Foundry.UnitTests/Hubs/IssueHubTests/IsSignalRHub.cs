using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.IssueHubTests;

public sealed class IsSignalRHub
{
    [Fact]
    public void WhenCreated_InheritsFromHub()
    {
        // Arrange
        IssueHub sut = new();

        // Act & Assert
        sut.ShouldBeAssignableTo<Hub>();
    }
}
