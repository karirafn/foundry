using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.CredentialTests;

public sealed class TokenExpiryChanges
{
    private static readonly AccountName Name = AccountName.Create("octocat").ValueOrThrow();
    private static readonly BaseUrl GitHubUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
    private static readonly BaseUrl GitLabUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
    private static readonly TokenExpiry FirstExpiry = new TokenExpiry.On(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));
    private static readonly TokenExpiry SecondExpiry = new TokenExpiry.On(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void WhenGitHubAccountIsCreatedWithAnIssuedToken_SetsTheTokenAndItsExpiry()
    {
        // Act
        GitHubCredential credential = GitHubCredential.Create(Name, new IssuedToken("ghp_first", FirstExpiry), GitHubUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Token.ShouldBe(new ProviderToken.Present("ghp_first")),
            () => credential.TokenExpiry.ShouldBe(FirstExpiry));
    }

    [Fact]
    public void WhenGitLabAccountIsCreatedWithAnIssuedToken_SetsTheTokenAndItsExpiry()
    {
        // Act
        GitLabCredential credential = GitLabCredential.Create(Name, new IssuedToken("glpat_first", new TokenExpiry.Never()), GitLabUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Token.ShouldBe(new ProviderToken.Present("glpat_first")),
            () => credential.TokenExpiry.ShouldBe(new TokenExpiry.Never()));
    }

    [Fact]
    public void WhenAccountIsCreatedWithoutAToken_ExpiryIsNotChecked()
    {
        // Act
        GitHubCredential credential = GitHubCredential.Create(Name, null, GitHubUrl);

        // Assert
        credential.TokenExpiry.ShouldBe(new TokenExpiry.NotChecked());
    }

    [Fact]
    public void WhenGitHubAccountIsUpdatedWithAnIssuedToken_ReplacesTheTokenAndItsExpiry()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(Name, new IssuedToken("ghp_first", FirstExpiry), GitHubUrl);

        // Act
        credential.Update(Name, new IssuedToken("ghp_second", SecondExpiry), GitHubUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Token.ShouldBe(new ProviderToken.Present("ghp_second")),
            () => credential.TokenExpiry.ShouldBe(SecondExpiry));
    }

    [Fact]
    public void WhenGitLabAccountIsUpdatedWithAnIssuedToken_ReplacesTheTokenAndItsExpiry()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create(Name, new IssuedToken("glpat_first", FirstExpiry), GitLabUrl);

        // Act
        credential.Update(Name, new IssuedToken("glpat_second", new TokenExpiry.Unavailable()), GitLabUrl);

        // Assert
        credential.ShouldSatisfyAllConditions(
            () => credential.Token.ShouldBe(new ProviderToken.Present("glpat_second")),
            () => credential.TokenExpiry.ShouldBe(new TokenExpiry.Unavailable()));
    }

    [Fact]
    public void WhenGitHubAccountIsUpdatedWithoutAToken_KeepsTheStoredExpiry()
    {
        // Arrange
        GitHubCredential credential = GitHubCredential.Create(Name, new IssuedToken("ghp_first", FirstExpiry), GitHubUrl);

        // Act
        credential.Update(AccountName.Create("renamed").ValueOrThrow(), null, BaseUrl.Create("https://github.example.com").ValueOrThrow());

        // Assert
        credential.TokenExpiry.ShouldBe(FirstExpiry);
    }

    [Fact]
    public void WhenGitLabAccountIsUpdatedWithoutAToken_KeepsTheStoredExpiry()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create(Name, new IssuedToken("glpat_first", FirstExpiry), GitLabUrl);

        // Act
        credential.Update(Name, null, BaseUrl.Create("https://gitlab.example.com").ValueOrThrow());

        // Assert
        credential.TokenExpiry.ShouldBe(FirstExpiry);
    }
}
