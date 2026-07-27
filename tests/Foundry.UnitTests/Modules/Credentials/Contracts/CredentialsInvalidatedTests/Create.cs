using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Contracts.CredentialsInvalidatedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        string reason = "Worker authentication failed";

        // Act
        CredentialsInvalidated @event = new(reason);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.Reason.ShouldBe(reason);
    }
}
