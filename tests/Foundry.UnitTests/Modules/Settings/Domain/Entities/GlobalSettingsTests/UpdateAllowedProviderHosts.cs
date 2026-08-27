using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Domain.Entities.GlobalSettingsTests;

public sealed class UpdateAllowedProviderHosts
{
    [Fact]
    public void WhenCreated_AllowedProviderHostsIsEmpty()
    {
        // Arrange / Act
        GlobalSettings settings = GlobalSettings.Create();

        // Assert
        settings.AllowedProviderHosts.ShouldBeEmpty();
    }

    [Fact]
    public void WhenValidHostProvided_ReturnsOk()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAllowedProviderHosts(["git.example.com"]);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenValidHostProvided_SetsAllowedProviderHosts()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdateAllowedProviderHosts(["git.example.com"]);

        // Assert
        settings.AllowedProviderHosts.ShouldBe(["git.example.com"]);
    }

    [Fact]
    public void WhenHostHasUpperCase_NormalizesToLowercase()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdateAllowedProviderHosts(["Git.Example.COM"]);

        // Assert
        settings.AllowedProviderHosts.ShouldBe(["git.example.com"]);
    }

    [Fact]
    public void WhenValidUpdate_UpdatesUpdatedAt()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        DateTimeOffset before = settings.UpdatedAt;

        // Act
        settings.UpdateAllowedProviderHosts(["git.example.com"]);

        // Assert
        settings.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenHostIsEmptyOrWhitespace_ReturnsInvalidProviderHostError(string host)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAllowedProviderHosts([host]);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidProviderHost");
    }

    [Theory]
    [InlineData("http://git.example.com")]
    [InlineData("https://git.example.com")]
    [InlineData("ssh://git.example.com")]
    public void WhenHostContainsScheme_ReturnsInvalidProviderHostError(string host)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAllowedProviderHosts([host]);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidProviderHost");
    }

    [Theory]
    [InlineData("git.example.com:8443")]
    [InlineData("git.example.com:443")]
    public void WhenHostContainsPort_ReturnsInvalidProviderHostError(string host)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAllowedProviderHosts([host]);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidProviderHost");
    }

    [Theory]
    [InlineData("git.example.com/path")]
    [InlineData("git.example.com/org/repo")]
    public void WhenHostContainsPath_ReturnsInvalidProviderHostError(string host)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAllowedProviderHosts([host]);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("Settings.InvalidProviderHost");
    }

    [Fact]
    public void WhenInvalidHostInList_DoesNotUpdateState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdateAllowedProviderHosts(["http://bad.host.com"]);

        // Assert
        settings.AllowedProviderHosts.ShouldBeEmpty();
    }

    [Fact]
    public void WhenEmptyList_ClearsAllowedProviderHosts()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateAllowedProviderHosts(["git.example.com"]);

        // Act
        settings.UpdateAllowedProviderHosts([]);

        // Assert
        settings.AllowedProviderHosts.ShouldBeEmpty();
    }

    [Fact]
    public void WhenMultipleValidHosts_SetsAll()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        settings.UpdateAllowedProviderHosts(["git.example.com", "gitlab.company.org"]);

        // Assert
        settings.AllowedProviderHosts.ShouldBe(["git.example.com", "gitlab.company.org"]);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("git.example.com.")]
    public void WhenHostEndsWithDot_ReturnsInvalidProviderHostError(string host)
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();

        // Act
        Result result = settings.UpdateAllowedProviderHosts([host]);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidProviderHostCode);
    }

    [Fact]
    public void WhenListExceedsMaxCount_ReturnsTooManyProviderHostsError()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        IReadOnlyList<string> hosts = Enumerable
            .Range(1, GlobalSettings.MaxAllowedProviderHostCount + 1)
            .Select(i => $"host{i}.example.com")
            .ToList();

        // Act
        Result result = settings.UpdateAllowedProviderHosts(hosts);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.TooManyProviderHostsCode);
    }

    [Fact]
    public void WhenListIsAtMaxCount_ReturnsOk()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        IReadOnlyList<string> hosts = Enumerable
            .Range(1, GlobalSettings.MaxAllowedProviderHostCount)
            .Select(i => $"host{i}.example.com")
            .ToList();

        // Act
        Result result = settings.UpdateAllowedProviderHosts(hosts);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void WhenHostExceedsMaxLength_ReturnsProviderHostTooLongError()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string host = new('a', GlobalSettings.MaxProviderHostLength + 1);

        // Act
        Result result = settings.UpdateAllowedProviderHosts([host]);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.ProviderHostTooLongCode);
    }

    [Fact]
    public void WhenHostIsAtMaxLength_ReturnsOk()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        string host = new('a', GlobalSettings.MaxProviderHostLength);

        // Act
        Result result = settings.UpdateAllowedProviderHosts([host]);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }
}
