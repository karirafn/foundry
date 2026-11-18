using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class IsDueForTokenRecheck
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static GitHubCredential CreateAccount() =>
        GitHubCredential.Create(
            AccountName.Create("my-org").ValueOrThrow(),
            new IssuedToken(string.Concat("ghp_", "token"), new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());

    [Fact]
    public void WhenTokenIsValid_ReturnsFalse()
    {
        // Arrange
        GitHubCredential credential = CreateAccount();

        // Act
        bool due = credential.IsDueForTokenRecheck(CheckedAt.AddDays(1));

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenInvalidAndCooldownHasNotElapsed_ReturnsFalse()
    {
        // Arrange
        GitHubCredential credential = CreateAccount();
        credential.MarkTokenInvalid(CheckedAt);

        // Act
        bool due = credential.IsDueForTokenRecheck(CheckedAt + Credential.TokenRecheckCooldown - TimeSpan.FromSeconds(1));

        // Assert
        due.ShouldBeFalse();
    }

    [Fact]
    public void WhenInvalidAndCooldownHasElapsed_ReturnsTrue()
    {
        // Arrange
        GitHubCredential credential = CreateAccount();
        credential.MarkTokenInvalid(CheckedAt);

        // Act
        bool due = credential.IsDueForTokenRecheck(CheckedAt + Credential.TokenRecheckCooldown);

        // Assert
        due.ShouldBeTrue();
    }

    [Fact]
    public void TokenRecheckCooldown_IsFifteenMinutes()
    {
        // Act
        TimeSpan cooldown = Credential.TokenRecheckCooldown;

        // Assert
        cooldown.ShouldBe(TimeSpan.FromMinutes(15));
    }
}
