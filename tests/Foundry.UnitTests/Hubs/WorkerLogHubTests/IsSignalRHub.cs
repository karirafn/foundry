using Foundry.WebApi.Hubs;

using Microsoft.AspNetCore.SignalR;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Hubs.WorkerLogHubTests;

public sealed class IsSignalRHub
{
    [Fact]
    public void WhenCreated_InheritsFromHub()
    {
        // Arrange
        WorkerLogHub sut = new();

        // Act & Assert
        sut.ShouldBeAssignableTo<Hub>();
    }
}
