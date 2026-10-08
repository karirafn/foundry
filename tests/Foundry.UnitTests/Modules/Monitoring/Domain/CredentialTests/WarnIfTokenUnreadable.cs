using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class WarnIfTokenUnreadable
{
    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();

    [Fact]
    public void WhenTokenIsUnreadable_LogsWarningContainingCredentialIdAndAccountsToken()
    {
        // Arrange
        CapturingLogger logger = new();
        GitHubCredential credential = GitHubCredential.CreateWithUnreadableToken("my-org", GitHubBaseUrl());

        // Act
        credential.WarnIfTokenUnreadable(logger);

        // Assert
        (LogLevel Level, string Message, Exception? Exception) entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldSatisfyAllConditions(
            () => entry.Message.ShouldContain(credential.Id.Value.ToString()),
            () => entry.Message.ShouldContain("accounts.token"));
    }

    [Fact]
    public void WhenTokenIsPresent_LogsNoWarning()
    {
        // Arrange
        CapturingLogger logger = new();
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_abc", GitHubBaseUrl());

        // Act
        credential.WarnIfTokenUnreadable(logger);

        // Assert
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void WhenTokenIsNull_LogsNoWarning()
    {
        // Arrange
        CapturingLogger logger = new();
        GitHubCredential credential = GitHubCredential.Create("my-org", null, GitHubBaseUrl());

        // Act
        credential.WarnIfTokenUnreadable(logger);

        // Assert
        logger.Entries.ShouldBeEmpty();
    }
}
