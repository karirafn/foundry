using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class CreateSeedsNotConfigured
{
    [Fact]
    public void WhenCreated_AuthModeIsApiKey()
    {
        // Arrange / Act
        ClaudeAccount account = ClaudeAccount.Create();

        // Assert
        account.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
    }

    [Fact]
    public void WhenCreated_ApiKeyCredentialIsNotConfigured()
    {
        // Arrange / Act
        ClaudeAccount account = ClaudeAccount.Create();

        // Assert
        AuthMode.ApiKey apiKey = account.AuthMode.ShouldBeOfType<AuthMode.ApiKey>();
        apiKey.Credential.ShouldBeOfType<ApiKeyCredential.NotConfigured>();
    }
}
