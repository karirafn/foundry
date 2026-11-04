using System.Runtime.Versioning;

using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.BuildContextSpoolTests;

public sealed class CreateStream
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task OnLinux_CreatesFileReadableAndWritableOnlyByOwner()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix file modes are only meaningful on Linux.");

        // Arrange & Act
        await using FileStream stream = BuildContextSpool.CreateStream();

        // Assert
        File.GetUnixFileMode(stream.Name).ShouldBe(OwnerReadWrite);
    }
}
