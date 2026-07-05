using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.AccountIdentityTests;

public sealed class Parse
{
    private const string ValidJson = """
        {
            "loggedIn": true,
            "authMethod": "claude.ai",
            "apiProvider": "firstParty",
            "email": "user@example.com",
            "orgId": "org-abc123",
            "orgName": "Example Org",
            "subscriptionType": "team"
        }
        """;

    [Fact]
    public void WhenValidJson_ReturnsSuccessWithFields()
    {
        // Arrange
        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(ValidJson);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        AccountIdentity identity = result.ShouldBeOfType<Result<AccountIdentity>.Success>().Value;
        identity.ShouldSatisfyAllConditions(
            () => identity.Email.ShouldBe("user@example.com"),
            () => identity.OrgName.ShouldBe("Example Org"),
            () => identity.SubscriptionType.ShouldBe("team"));
    }

    [Fact]
    public void WhenLoggedInIsFalse_ReturnsFailure()
    {
        // Arrange
        string json = """
            {
                "loggedIn": false,
                "authMethod": "none",
                "apiProvider": "none",
                "email": "",
                "orgId": "",
                "orgName": "",
                "subscriptionType": ""
            }
            """;

        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(json);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenJsonIsInvalid_ReturnsFailure()
    {
        // Arrange
        const string json = "not valid json {{";

        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(json);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenJsonIsEmpty_ReturnsFailure()
    {
        // Arrange
        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(string.Empty);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenJsonMissingLoggedInField_ReturnsFailure()
    {
        // Arrange
        string json = """
            {
                "email": "user@example.com",
                "orgName": "Example Org",
                "subscriptionType": "team"
            }
            """;

        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(json);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void WhenEmailExceedsCap_TruncatesToCap()
    {
        // Arrange
        string oversizedEmail = new string('a', AccountIdentity.MaxEmailLength + 100);
        string json = $$"""
            {
                "loggedIn": true,
                "email": "{{oversizedEmail}}",
                "orgName": "Example Org",
                "subscriptionType": "team"
            }
            """;

        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(json);

        // Assert
        AccountIdentity identity = result.ShouldBeOfType<Result<AccountIdentity>.Success>().Value;
        identity.Email.Length.ShouldBe(AccountIdentity.MaxEmailLength);
    }

    [Fact]
    public void WhenOrgNameExceedsCap_TruncatesToCap()
    {
        // Arrange
        string oversizedOrgName = new string('a', AccountIdentity.MaxOrgNameLength + 100);
        string json = $$"""
            {
                "loggedIn": true,
                "email": "user@example.com",
                "orgName": "{{oversizedOrgName}}",
                "subscriptionType": "team"
            }
            """;

        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(json);

        // Assert
        AccountIdentity identity = result.ShouldBeOfType<Result<AccountIdentity>.Success>().Value;
        identity.OrgName.Length.ShouldBe(AccountIdentity.MaxOrgNameLength);
    }

    [Fact]
    public void WhenSubscriptionTypeExceedsCap_TruncatesToCap()
    {
        // Arrange
        string oversizedType = new string('a', AccountIdentity.MaxSubscriptionTypeLength + 100);
        string json = $$"""
            {
                "loggedIn": true,
                "email": "user@example.com",
                "orgName": "Example Org",
                "subscriptionType": "{{oversizedType}}"
            }
            """;

        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(json);

        // Assert
        AccountIdentity identity = result.ShouldBeOfType<Result<AccountIdentity>.Success>().Value;
        identity.SubscriptionType.Length.ShouldBe(AccountIdentity.MaxSubscriptionTypeLength);
    }

    [Fact]
    public void WhenEmailAtExactCap_IsAcceptedUnchanged()
    {
        // Arrange
        string exactEmail = new string('a', AccountIdentity.MaxEmailLength);
        string json = $$"""
            {
                "loggedIn": true,
                "email": "{{exactEmail}}",
                "orgName": "Example Org",
                "subscriptionType": "team"
            }
            """;

        // Act
        Result<AccountIdentity> result = AccountIdentity.Parse(json);

        // Assert
        AccountIdentity identity = result.ShouldBeOfType<Result<AccountIdentity>.Success>().Value;
        identity.Email.Length.ShouldBe(AccountIdentity.MaxEmailLength);
    }
}
