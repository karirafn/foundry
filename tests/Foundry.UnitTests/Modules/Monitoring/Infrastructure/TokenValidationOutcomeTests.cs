using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure;

public sealed class TokenValidationOutcomeTests
{
    [Fact]
    public void WhenAuthenticated_AccountNameIsRequired_AndMissingScopesDefaultsToEmpty()
    {
        // Arrange
        const string AccountName = "octocat";

        // Act
        TokenValidationOutcome outcome = TokenValidationOutcome.Authenticated(AccountName, new TokenExpiry.NotChecked());

        // Assert
        TokenValidationOutcome.AuthenticatedOutcome authenticated =
            outcome.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        authenticated.ShouldSatisfyAllConditions(
            () => authenticated.AccountName.ShouldBe(AccountName),
            () => authenticated.MissingScopes.ShouldBeEmpty());
    }

    [Fact]
    public void WhenAuthenticated_WithMissingScopes_CarriesMissingScopes()
    {
        // Arrange
        const string AccountName = "octocat";
        IReadOnlyList<string> missingScopes = ["repo", "workflow"];

        // Act
        TokenValidationOutcome outcome = TokenValidationOutcome.Authenticated(AccountName, new TokenExpiry.NotChecked(), missingScopes);

        // Assert
        TokenValidationOutcome.AuthenticatedOutcome authenticated =
            outcome.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        authenticated.MissingScopes.ShouldBe(missingScopes);
    }

    [Fact]
    public void WhenAuthenticationFailed_ProducesAuthenticationFailedVariant()
    {
        // Arrange

        // Act
        TokenValidationOutcome outcome = TokenValidationOutcome.AuthenticationFailed();

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.AuthenticationFailedOutcome>();
    }

    [Fact]
    public void WhenGrantRead_AccountNameAndGrantAreCarried()
    {
        // Arrange
        const string AccountName = "octocat";
        GitLabTokenGrant grant = GitLabTokenGrant.Create([]);

        // Act
        TokenValidationOutcome outcome = TokenValidationOutcome.GrantRead(AccountName, grant, new TokenExpiry.NotChecked());

        // Assert
        TokenValidationOutcome.GrantReadOutcome grantRead = outcome.ShouldBeOfType<TokenValidationOutcome.GrantReadOutcome>();
        grantRead.ShouldSatisfyAllConditions(
            () => grantRead.AccountName.ShouldBe(AccountName),
            () => grantRead.Grant.ShouldBeSameAs(grant));
    }

    [Fact]
    public void WhenIdentityUnresolved_ProducesIdentityUnresolvedVariant()
    {
        // Arrange

        // Act
        TokenValidationOutcome outcome = TokenValidationOutcome.IdentityUnresolved();

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.IdentityUnresolvedOutcome>();
    }

    [Fact]
    public void WhenProviderMismatch_DetectedProviderIsRequired_AndIsCarried()
    {
        // Arrange
        const string DetectedProvider = "gitlab";

        // Act
        TokenValidationOutcome outcome = TokenValidationOutcome.ProviderMismatch(DetectedProvider);

        // Assert
        TokenValidationOutcome.ProviderMismatchOutcome providerMismatch =
            outcome.ShouldBeOfType<TokenValidationOutcome.ProviderMismatchOutcome>();
        providerMismatch.DetectedProvider.ShouldBe(DetectedProvider);
    }

    [Fact]
    public void WhenPatternMatched_AllVariantsAreExhaustive()
    {
        // Arrange
        TokenValidationOutcome[] outcomes =
        [
            TokenValidationOutcome.Authenticated("octocat", new TokenExpiry.NotChecked()),
            TokenValidationOutcome.AuthenticationFailed(),
            TokenValidationOutcome.GrantRead("octocat", GitLabTokenGrant.Create([]), new TokenExpiry.NotChecked()),
            TokenValidationOutcome.TokenOfWrongKind("octocat", WrongTokenKind.GitHubClassic, new TokenExpiry.NotChecked()),
            TokenValidationOutcome.IdentityUnresolved(),
            TokenValidationOutcome.ProviderMismatch("gitlab"),
        ];

        // Act + Assert — each variant must match exactly one arm
        foreach (TokenValidationOutcome outcome in outcomes)
        {
            string label = outcome switch
            {
                TokenValidationOutcome.AuthenticatedOutcome => "authenticated",
                TokenValidationOutcome.AuthenticationFailedOutcome => "authenticationFailed",
                TokenValidationOutcome.GrantReadOutcome => "grantRead",
                TokenValidationOutcome.WrongTokenKindOutcome => "wrongTokenKind",
                TokenValidationOutcome.IdentityUnresolvedOutcome => "identityUnresolved",
                TokenValidationOutcome.ProviderMismatchOutcome => "providerMismatch",
                _ => throw new InvalidOperationException($"Unhandled: {outcome.GetType().Name}"),
            };

            label.ShouldNotBeEmpty();
        }
    }
}
