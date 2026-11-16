using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ToolchainTests;

public sealed class TryCreate
{
    [Fact]
    public void WhenTheEntriesAreWithinTheLimit_KeepsThemInOrder()
    {
        // Arrange
        ToolchainEntry node = ToolchainEntry.TryCreate("node", "22.22.3").ShouldNotBeNull();
        ToolchainEntry git = ToolchainEntry.TryCreate("git", "2.39.5").ShouldNotBeNull();

        // Act
        Toolchain? toolchain = Toolchain.TryCreate([node, git]);

        // Assert
        toolchain.ShouldNotBeNull().Entries.ShouldBe([node, git]);
    }

    [Fact]
    public void WhenThereAreExactlyTheMaximumEntries_IsKept()
    {
        // Arrange
        ToolchainEntry[] entries = [.. Enumerable.Range(0, Toolchain.MaxEntries)
            .Select(i => ToolchainEntry.TryCreate($"tool-{i}", "1.0").ShouldNotBeNull())];

        // Act
        Toolchain? toolchain = Toolchain.TryCreate(entries);

        // Assert
        toolchain.ShouldNotBeNull().Entries.Count.ShouldBe(Toolchain.MaxEntries);
    }

    [Fact]
    public void WhenThereAreMoreThanTheMaximumEntries_ReturnsNull()
    {
        // Arrange
        ToolchainEntry[] entries = [.. Enumerable.Range(0, Toolchain.MaxEntries + 1)
            .Select(i => ToolchainEntry.TryCreate($"tool-{i}", "1.0").ShouldNotBeNull())];

        // Act
        Toolchain? toolchain = Toolchain.TryCreate(entries);

        // Assert
        toolchain.ShouldBeNull();
    }

    [Fact]
    public void WhenThereAreNoEntries_IsAnEmptyToolchain()
    {
        // Act
        Toolchain? toolchain = Toolchain.TryCreate([]);

        // Assert
        toolchain.ShouldNotBeNull().Entries.ShouldBeEmpty();
    }
}
