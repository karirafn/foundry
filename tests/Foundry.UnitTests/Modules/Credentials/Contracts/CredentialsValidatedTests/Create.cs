using Foundry.Modules.Credentials.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Contracts.CredentialsValidatedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        string email = "user@example.com";
        string orgName = "ExampleOrg";
        string subscriptionType = "Pro";

        // Act
        CredentialsValidated @event = new(email, orgName, subscriptionType);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.ShouldSatisfyAllConditions(
            () => @event.Email.ShouldBe(email),
            () => @event.OrgName.ShouldBe(orgName),
            () => @event.SubscriptionType.ShouldBe(subscriptionType));
    }

    [Fact]
    public void WhenCreatedWithNullValues_AllowsNulls()
    {
        // Arrange & Act
        CredentialsValidated @event = new(Email: null, OrgName: null, SubscriptionType: null);

        // Assert
        @event.ShouldSatisfyAllConditions(
            () => @event.Email.ShouldBeNull(),
            () => @event.OrgName.ShouldBeNull(),
            () => @event.SubscriptionType.ShouldBeNull());
    }
}
