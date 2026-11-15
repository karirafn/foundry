using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ToolchainTests;

public sealed class Has
{
    private static Toolchain WithNodeAndDocker() =>
        Toolchain.TryCreate([
            ToolchainEntry.TryCreate("node", "22.22.3").ShouldNotBeNull(),
            ToolchainEntry.TryCreate("docker", "29.9.3").ShouldNotBeNull(),
        ]).ShouldNotBeNull();

    [Fact]
    public void WhenTheToolIsListed_ReturnsTrue()
    {
        WithNodeAndDocker().Has("docker").ShouldBeTrue();
    }

    [Fact]
    public void WhenTheToolIsNotListed_ReturnsFalse()
    {
        WithNodeAndDocker().Has("csharp-ls").ShouldBeFalse();
    }

    [Fact]
    public void WhenTheNameDiffersOnlyInCase_ReturnsFalse()
    {
        WithNodeAndDocker().Has("Docker").ShouldBeFalse();
    }

    [Fact]
    public void WhenTheToolchainIsUnrecorded_HasNoTool()
    {
        Toolchain.Unrecorded.Has("docker").ShouldBeFalse();
        Toolchain.Unrecorded.Has("csharp-ls").ShouldBeFalse();
    }
}
