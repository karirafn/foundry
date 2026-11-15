using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.ToolchainLineParserTests;

public sealed class Parse
{
    // The line workers/toolchain.sh printed in a foundry-worker image on 2026-11-15 (every toolchain installed, no
    // playwright-core).
    private const string CapturedLine =
        """FOUNDRY_TOOLCHAIN [{"name":"node","version":"22.22.3"},{"name":"claude-code","version":"2.1.319"},{"name":"dotnet-sdk","version":"10.0.404"},{"name":"csharp-ls","version":"0.28.0"},{"name":"angular-cli","version":"21.2.30"},{"name":"chromium","version":"156.0.8078.79"},{"name":"docker","version":"29.9.3"},{"name":"gh","version":"2.103.3"},{"name":"glab","version":"1.127.0"},{"name":"git","version":"2.39.5"}]""";

    [Fact]
    public void WhenTheLogHoldsOneValidLine_ReturnsItsEntriesInOrder()
    {
        // Act
        Toolchain? toolchain = ToolchainLineParser.Parse(CapturedLine + "\n");

        // Assert
        toolchain.ShouldNotBeNull();
        toolchain.Entries.Select(e => e.Name).ShouldBe(
            ["node", "claude-code", "dotnet-sdk", "csharp-ls", "angular-cli", "chromium", "docker", "gh", "glab", "git"]);
        toolchain.Entries[3].Version.ShouldBe("0.28.0");
    }

    [Fact]
    public void WhenTheLineCarriesADockerTimestamp_ReadsPastIt()
    {
        // Act
        Toolchain? toolchain = ToolchainLineParser.Parse($"2026-11-15T07:40:00.123456789Z {CapturedLine}\r\n");

        // Assert
        toolchain.ShouldNotBeNull().Entries.Count.ShouldBe(10);
    }

    [Fact]
    public void WhenOtherLinesSurroundIt_ReadsOnlyTheToolchainLine()
    {
        // Act
        Toolchain? toolchain = ToolchainLineParser.Parse($"noise\n{CapturedLine}\nmore noise\n");

        // Assert
        toolchain.ShouldNotBeNull().Entries.Count.ShouldBe(10);
    }

    [Fact]
    public void WhenTheListIsEmpty_ReturnsAnEmptyToolchain()
    {
        // Act
        Toolchain? toolchain = ToolchainLineParser.Parse("FOUNDRY_TOOLCHAIN []\n");

        // Assert
        toolchain.ShouldNotBeNull().Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bash: /opt/foundry/toolchain.sh: No such file or directory\n")]
    [InlineData("echo FOUNDRY_TOOLCHAIN []\n")]
    public void WhenThereIsNoLine_ReturnsNull(string? log)
    {
        ToolchainLineParser.Parse(log).ShouldBeNull();
    }

    [Fact]
    public void WhenTheLineAppearsTwice_ReturnsNull()
    {
        ToolchainLineParser.Parse($"{CapturedLine}\n{CapturedLine}\n").ShouldBeNull();
    }

    [Theory]
    [InlineData("FOUNDRY_TOOLCHAIN [{\"name\":\"node\"")]
    [InlineData("FOUNDRY_TOOLCHAIN {\"name\":\"node\",\"version\":\"1\"}")]
    [InlineData("FOUNDRY_TOOLCHAIN [{\"name\":\"node\",\"version\":1}]")]
    [InlineData("FOUNDRY_TOOLCHAIN [\"node\"]")]
    [InlineData("FOUNDRY_TOOLCHAIN [null]")]
    [InlineData("FOUNDRY_TOOLCHAIN null")]
    [InlineData("FOUNDRY_TOOLCHAIN ")]
    public void WhenTheJsonIsInvalid_ReturnsNull(string line)
    {
        ToolchainLineParser.Parse(line + "\n").ShouldBeNull();
    }

    [Fact]
    public void WhenAnEntryHasAnEmptyName_ReturnsNull()
    {
        ToolchainLineParser.Parse("""FOUNDRY_TOOLCHAIN [{"name":"","version":"1.0"}]""").ShouldBeNull();
    }

    [Fact]
    public void WhenAnEntryHasAnEmptyVersion_ReturnsNull()
    {
        ToolchainLineParser.Parse("""FOUNDRY_TOOLCHAIN [{"name":"node","version":""}]""").ShouldBeNull();
    }

    [Fact]
    public void WhenAnEntryNameIsOver64Characters_ReturnsNull()
    {
        string name = new('n', 65);
        ToolchainLineParser.Parse($$"""FOUNDRY_TOOLCHAIN [{"name":"{{name}}","version":"1.0"}]""").ShouldBeNull();
    }

    [Fact]
    public void WhenAnEntryVersionIsOver128Characters_ReturnsNull()
    {
        string version = new('1', 129);
        ToolchainLineParser.Parse($$"""FOUNDRY_TOOLCHAIN [{"name":"node","version":"{{version}}"}]""").ShouldBeNull();
    }

    [Fact]
    public void WhenThereAreMoreThan32Entries_ReturnsNull()
    {
        string entries = string.Join(',', Enumerable.Range(0, 33).Select(i => $$"""{"name":"tool-{{i}}","version":"1.0"}"""));
        ToolchainLineParser.Parse($"FOUNDRY_TOOLCHAIN [{entries}]").ShouldBeNull();
    }

    [Fact]
    public void WhenThereAreExactly32Entries_ReturnsThem()
    {
        string entries = string.Join(',', Enumerable.Range(0, 32).Select(i => $$"""{"name":"tool-{{i}}","version":"1.0"}"""));
        ToolchainLineParser.Parse($"FOUNDRY_TOOLCHAIN [{entries}]").ShouldNotBeNull().Entries.Count.ShouldBe(32);
    }
}
