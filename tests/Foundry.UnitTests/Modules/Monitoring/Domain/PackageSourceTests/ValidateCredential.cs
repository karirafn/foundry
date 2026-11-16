using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.PackageSourceTests;

public sealed class ValidateCredential
{
    [Fact]
    public void WhenBothFieldsAreWithinTheirLimits_HasNoErrors()
    {
        // Act
        IReadOnlyList<PackageSourceCredentialFieldError> errors =
            PackageSourceCredential.Validate(new string('u', 255), new string('t', 2048));

        // Assert
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void WhenBothFieldsAreMissing_ReportsEachField()
    {
        // Act
        IReadOnlyList<PackageSourceCredentialFieldError> errors = PackageSourceCredential.Validate(null, "");

        // Assert
        errors.ShouldBe([
            new PackageSourceCredentialFieldError(PackageSourceCredentialField.Username, "Username is required."),
            new PackageSourceCredentialFieldError(PackageSourceCredentialField.Token, "Token is required."),
        ]);
    }

    [Fact]
    public void WhenBothFieldsAreTooLong_ReportsEachField()
    {
        // Act
        IReadOnlyList<PackageSourceCredentialFieldError> errors =
            PackageSourceCredential.Validate(new string('u', 256), new string('t', 2049));

        // Assert
        errors.ShouldBe([
            new PackageSourceCredentialFieldError(
                PackageSourceCredentialField.Username,
                "Username must be 255 characters or fewer."),
            new PackageSourceCredentialFieldError(
                PackageSourceCredentialField.Token,
                "Token must be 2048 characters or fewer."),
        ]);
    }

    [Fact]
    public void WhenTheUsernameIsWhitespace_AcceptsItAsGiven()
    {
        // Act
        IReadOnlyList<PackageSourceCredentialFieldError> errors = PackageSourceCredential.Validate(" ", "token");

        // Assert
        errors.ShouldBeEmpty();
    }
}
