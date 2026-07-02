using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Features.Login;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.OnboardingSeedTests;

public sealed class Merge
{
    private const string WorkDir = "/home/node";

    [Fact]
    public void WhenInputIsNull_ReturnsFreshSeedWithDefaults()
    {
        // Arrange
        // Act
        string result = OnboardingSeed.Merge(null, WorkDir);

        // Assert
        JsonObject json = JsonNode.Parse(result)!.AsObject();
        json["hasCompletedOnboarding"]!.GetValue<bool>().ShouldBeTrue();
        json["theme"]!.GetValue<string>().ShouldBe("dark");
        json["projects"]![WorkDir]!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void WhenInputIsEmpty_ReturnsFreshSeedWithDefaults()
    {
        // Arrange
        // Act
        string result = OnboardingSeed.Merge(string.Empty, WorkDir);

        // Assert
        JsonObject json = JsonNode.Parse(result)!.AsObject();
        json["hasCompletedOnboarding"]!.GetValue<bool>().ShouldBeTrue();
        json["theme"]!.GetValue<string>().ShouldBe("dark");
        json["projects"]![WorkDir]!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void WhenInputIsMalformed_ReturnsFreshSeedWithDefaults()
    {
        // Arrange
        const string malformed = "not valid json {{{}}}";

        // Act
        string result = OnboardingSeed.Merge(malformed, WorkDir);

        // Assert
        JsonObject json = JsonNode.Parse(result)!.AsObject();
        json["hasCompletedOnboarding"]!.GetValue<bool>().ShouldBeTrue();
        json["theme"]!.GetValue<string>().ShouldBe("dark");
        json["projects"]![WorkDir]!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void WhenInputHasOauthAccount_PreservesItAndAddsFlags()
    {
        // Arrange
        string existing = """
            {
                "oauthAccount": {
                    "emailAddress": "user@example.com",
                    "organizationId": "org-123"
                }
            }
            """;

        // Act
        string result = OnboardingSeed.Merge(existing, WorkDir);

        // Assert
        JsonObject json = JsonNode.Parse(result)!.AsObject();
        json["oauthAccount"].ShouldNotBeNull();
        json["oauthAccount"]!["emailAddress"]!.GetValue<string>().ShouldBe("user@example.com");
        json["hasCompletedOnboarding"]!.GetValue<bool>().ShouldBeTrue();
        json["theme"]!.GetValue<string>().ShouldBe("dark");
        json["projects"]![WorkDir]!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void WhenAlreadySeeded_IsIdempotent()
    {
        // Arrange
        string firstPass = OnboardingSeed.Merge(null, WorkDir);

        // Act
        string result = OnboardingSeed.Merge(firstPass, WorkDir);

        // Assert
        // Parse both and compare the key values — order is not guaranteed
        JsonObject first = JsonNode.Parse(firstPass)!.AsObject();
        JsonObject second = JsonNode.Parse(result)!.AsObject();
        second["hasCompletedOnboarding"]!.GetValue<bool>().ShouldBe(
            first["hasCompletedOnboarding"]!.GetValue<bool>());
        second["theme"]!.GetValue<string>().ShouldBe(
            first["theme"]!.GetValue<string>());
        second["projects"]![WorkDir]!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBe(
            first["projects"]![WorkDir]!["hasTrustDialogAccepted"]!.GetValue<bool>());
    }

    [Fact]
    public void WhenExistingJsonHasOtherKeys_DoesNotClobberThem()
    {
        // Arrange
        string existing = """
            {
                "someUserSetting": "keep me",
                "theme": "light"
            }
            """;

        // Act
        string result = OnboardingSeed.Merge(existing, WorkDir);

        // Assert
        JsonObject json = JsonNode.Parse(result)!.AsObject();
        json["someUserSetting"]!.GetValue<string>().ShouldBe("keep me");
        // theme was already present — must not be clobbered
        json["theme"]!.GetValue<string>().ShouldBe("light");
    }
}
