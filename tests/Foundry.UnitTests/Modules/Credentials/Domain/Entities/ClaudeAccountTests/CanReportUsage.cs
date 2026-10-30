using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class CanReportUsage
{
    [Fact]
    public void WhenOAuthWithCommittedIdentityAndValidValidity_ReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Act
        bool result = account.CanReportUsage;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyMode_ReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-test")));

        // Act
        bool result = account.CanReportUsage;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenValidityIsInvalid_ReturnsFalse()
    {
        // Arrange — OAuth with committed identity; validity check fires.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.Invalidate("worker_auth_failed");

        // Act
        bool result = account.CanReportUsage;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenOAuthWithNoCommittedIdentity_ReturnsFalse()
    {
        // Arrange — OAuth mode with null email means no committed identity.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin(null, null, "pro");

        // Act
        bool result = account.CanReportUsage;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenSpendIsBlocked_ReturnsTrue()
    {
        // Arrange — spend state does not gate usage reporting.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.BlockSpend(DateTimeOffset.UtcNow.AddMinutes(5));

        // Act
        bool result = account.CanReportUsage;

        // Assert
        result.ShouldBeTrue();
    }
}
