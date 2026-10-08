using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.ValueObjects.ApiKeyCredentialTests;

public sealed class Construct
{
    [Fact]
    public void WhenPresentCreated_ExposesValue()
    {
        // Arrange
        const string key = "sk-ant-api03-test";

        // Act
        ApiKeyCredential credential = new ApiKeyCredential.Present(key);

        // Assert
        ApiKeyCredential.Present present = credential.ShouldBeOfType<ApiKeyCredential.Present>();
        present.Value.ShouldBe(key);
    }

    [Fact]
    public void WhenPresentCreatedWithNull_ThrowsArgumentException()
    {
        // Arrange / Act / Assert
        Should.Throw<ArgumentException>(() => new ApiKeyCredential.Present(null!));
    }

    [Fact]
    public void WhenPresentCreatedWithWhitespace_ThrowsArgumentException()
    {
        // Arrange / Act / Assert
        Should.Throw<ArgumentException>(() => new ApiKeyCredential.Present("   "));
    }

    [Fact]
    public void WhenPresentToStringCalled_DoesNotContainKeyValue()
    {
        // Arrange
        const string key = "sk-ant-api03-secret-key";
        ApiKeyCredential.Present present = new(key);

        // Act
        string text = present.ToString();

        // Assert
        text.ShouldBe("Present { Value = *** }");
        text.ShouldNotContain(key);
    }

    [Fact]
    public void WhenNotConfiguredCreated_IsNotConfiguredVariant()
    {
        // Arrange / Act
        ApiKeyCredential credential = new ApiKeyCredential.NotConfigured();

        // Assert
        credential.ShouldBeOfType<ApiKeyCredential.NotConfigured>();
    }

    [Fact]
    public void WhenUnreadableCreated_IsUnreadableVariant()
    {
        // Arrange / Act
        ApiKeyCredential credential = new ApiKeyCredential.Unreadable();

        // Assert
        credential.ShouldBeOfType<ApiKeyCredential.Unreadable>();
    }

    [Fact]
    public void WhenTwoPresentWithSameValue_AreEqual()
    {
        // Arrange
        ApiKeyCredential a = new ApiKeyCredential.Present("sk-ant-api03-test");
        ApiKeyCredential b = new ApiKeyCredential.Present("sk-ant-api03-test");

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenTwoPresentWithDifferentValues_AreNotEqual()
    {
        // Arrange
        ApiKeyCredential a = new ApiKeyCredential.Present("key-one");
        ApiKeyCredential b = new ApiKeyCredential.Present("key-two");

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenPresentAndNotConfigured_AreNotEqual()
    {
        // Arrange
        ApiKeyCredential a = new ApiKeyCredential.Present("any-key");
        ApiKeyCredential b = new ApiKeyCredential.NotConfigured();

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenTwoNotConfigured_AreEqual()
    {
        // Arrange
        ApiKeyCredential a = new ApiKeyCredential.NotConfigured();
        ApiKeyCredential b = new ApiKeyCredential.NotConfigured();

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenTwoUnreadable_AreEqual()
    {
        // Arrange
        ApiKeyCredential a = new ApiKeyCredential.Unreadable();
        ApiKeyCredential b = new ApiKeyCredential.Unreadable();

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenUnreadableAndNotConfigured_AreNotEqual()
    {
        // Arrange
        ApiKeyCredential a = new ApiKeyCredential.Unreadable();
        ApiKeyCredential b = new ApiKeyCredential.NotConfigured();

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenPresentPatternMatched_ExtractsValue()
    {
        // Arrange
        ApiKeyCredential present = new ApiKeyCredential.Present("sk-ant-api03-test");

        // Act
        string result = present is ApiKeyCredential.Present p ? p.Value : "wrong";

        // Assert
        result.ShouldBe("sk-ant-api03-test");
    }

    [Fact]
    public void WhenNotConfiguredPatternMatched_MatchesNotConfiguredVariant()
    {
        // Arrange
        ApiKeyCredential notConfigured = new ApiKeyCredential.NotConfigured();

        // Act
        bool matched = notConfigured is ApiKeyCredential.NotConfigured;

        // Assert
        matched.ShouldBeTrue();
    }

    [Fact]
    public void WhenUnreadablePatternMatched_MatchesUnreadableVariant()
    {
        // Arrange
        ApiKeyCredential unreadable = new ApiKeyCredential.Unreadable();

        // Act
        bool matched = unreadable is ApiKeyCredential.Unreadable;

        // Assert
        matched.ShouldBeTrue();
    }
}
