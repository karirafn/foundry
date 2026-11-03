using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using ProviderToken = Foundry.Modules.Monitoring.Domain.ValueObjects.ProviderToken;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialBranchRulesTokenTests;

public sealed class BranchRulesToken
{
    private static BaseUrl GitHubUrl => BaseUrl.Create("https://github.com").ValueOrThrow();

    private static BaseUrl GitLabUrl => BaseUrl.Create("https://gitlab.com").ValueOrThrow();

    [Fact]
    public void WhenCreatedWithoutBranchRulesToken_IsAbsent()
    {
        // Arrange / Act
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.BranchRulesToken.ShouldBeNull(),
            () => credential.IsBranchRulesTokenUnreadable.ShouldBeFalse());
    }

    [Fact]
    public void WhenGitHubCreatedWithBranchRulesToken_StoresItSeparatelyFromAccountToken()
    {
        // Arrange / Act
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl, "ghp_rules");

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Token.ShouldBe(new ProviderToken.Present("ghp_worker")),
            () => credential.BranchRulesToken.ShouldBe(new ProviderToken.Present("ghp_rules")));
    }

    [Fact]
    public void WhenGitLabCreatedWithBranchRulesToken_StoresIt()
    {
        // Arrange / Act
        GitLabCredential credential = GitLabCredential.Create("my-group", "glpat-worker", GitLabUrl, "glpat-rules");

        // Assert
        credential.BranchRulesToken.ShouldBe(new ProviderToken.Present("glpat-rules"));
    }

    [Fact]
    public void WhenSet_ReplacesExistingToken()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl, "ghp_old_rules");

        // Act
        credential.SetBranchRulesToken("ghp_new_rules");

        // Assert
        credential.BranchRulesToken.ShouldBe(new ProviderToken.Present("ghp_new_rules"));
    }

    [Fact]
    public void WhenSetOnUnreadableToken_RepairsIt()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl);
        Credential.ApplyUnreadableBranchRulesToken(credential);
        credential.IsBranchRulesTokenUnreadable.ShouldBeTrue();

        // Act
        credential.SetBranchRulesToken("ghp_fresh_rules");

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.IsBranchRulesTokenUnreadable.ShouldBeFalse(),
            () => credential.BranchRulesToken.ShouldBe(new ProviderToken.Present("ghp_fresh_rules")));
    }

    [Fact]
    public void WhenBranchRulesTokenUnreadable_AccountTokenRemainsReadable()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl);

        // Act
        Credential.ApplyUnreadableBranchRulesToken(credential);

        // Assert — an unreadable rules token never makes the account itself ineligible.
        credential.IsTokenUnreadable.ShouldBeFalse();
    }

    [Fact]
    public void WhenCleared_RemovesToken()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl, "ghp_rules");

        // Act
        credential.ClearBranchRulesToken();

        // Assert
        credential.BranchRulesToken.ShouldBeNull();
    }

    [Fact]
    public void WhenAccountUpdatedWithNewToken_BranchRulesTokenIsPreserved()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl, "ghp_rules");

        // Act — account rotation replaces only the account token.
        credential.Update("my-org", "ghp_rotated", GitHubUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Token.ShouldBe(new ProviderToken.Present("ghp_rotated")),
            () => credential.BranchRulesToken.ShouldBe(new ProviderToken.Present("ghp_rules")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenSetToBlank_Throws(string token)
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_worker", GitHubUrl);

        // Act / Assert
        Should.Throw<ArgumentException>(() => credential.SetBranchRulesToken(token));
    }
}
