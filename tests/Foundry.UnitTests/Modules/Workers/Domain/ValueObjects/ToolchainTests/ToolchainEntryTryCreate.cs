using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ToolchainTests;

public sealed class ToolchainEntryTryCreate
{
    [Fact]
    public void WhenNameAndVersionAreValid_KeepsThem()
    {
        // Act
        ToolchainEntry? entry = ToolchainEntry.TryCreate("csharp-ls", "0.28.0");

        // Assert
        entry.ShouldNotBeNull();
        entry.Name.ShouldBe("csharp-ls");
        entry.Version.ShouldBe("0.28.0");
    }

    [Fact]
    public void WhenNameAndVersionAreExactlyTheMaximumLength_AreKept()
    {
        // Arrange
        string name = new('n', ToolchainEntry.MaxNameLength);
        string version = new('1', ToolchainEntry.MaxVersionLength);

        // Act
        ToolchainEntry? entry = ToolchainEntry.TryCreate(name, version);

        // Assert
        entry.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WhenTheNameIsMissingOrEmpty_ReturnsNull(string? name)
    {
        ToolchainEntry.TryCreate(name, "1.0").ShouldBeNull();
    }

    [Fact]
    public void WhenTheNameIsTooLong_ReturnsNull()
    {
        ToolchainEntry.TryCreate(new string('n', ToolchainEntry.MaxNameLength + 1), "1.0").ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WhenTheVersionIsMissingOrEmpty_ReturnsNull(string? version)
    {
        ToolchainEntry.TryCreate("node", version).ShouldBeNull();
    }

    [Fact]
    public void WhenTheVersionIsTooLong_ReturnsNull()
    {
        ToolchainEntry.TryCreate("node", new string('1', ToolchainEntry.MaxVersionLength + 1)).ShouldBeNull();
    }
}
