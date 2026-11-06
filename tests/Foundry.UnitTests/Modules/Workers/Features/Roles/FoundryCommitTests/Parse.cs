using Foundry.Modules.Workers.Features.Roles;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.FoundryCommitTests;

public sealed class Parse
{
    [Theory]
    [InlineData("1.0.0+71c3f21167b28c2ed2e3e1a81e2dcbc9f66eb4c2", "71c3f21167b28c2ed2e3e1a81e2dcbc9f66eb4c2")]
    [InlineData("1.0.0", null)]
    [InlineData("1.0.0+", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void WhenParsingTheInformationalVersion_ReturnsTheSourceRevision(string? informationalVersion, string? expected)
    {
        // Act
        string? commit = FoundryCommit.Parse(informationalVersion);

        // Assert
        commit.ShouldBe(expected);
    }
}
