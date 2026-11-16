using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.WorkerImageToolchainTests;

public sealed class Create
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private static readonly Toolchain DockerOnly =
        Toolchain.TryCreate([ToolchainEntry.TryCreate("docker", "29.9.3").ShouldNotBeNull()]).ShouldNotBeNull();

    [Fact]
    public void WhenTheImageIdIsValid_KeepsTheImageToolchainAndTime()
    {
        // Act
        WorkerImageToolchain row = WorkerImageToolchain.Create("sha256:abc", DockerOnly, RecordedAt);

        // Assert
        row.ImageId.ShouldBe("sha256:abc");
        row.Toolchain.Has("docker").ShouldBeTrue();
        row.RecordedAt.ShouldBe(RecordedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenTheImageIdIsBlank_Throws(string imageId)
    {
        Should.Throw<ArgumentException>(() => WorkerImageToolchain.Create(imageId, DockerOnly, RecordedAt));
    }

    [Fact]
    public void WhenTheImageIdIsTooLong_Throws()
    {
        string imageId = new('a', WorkerImageToolchain.MaxImageIdLength + 1);

        Should.Throw<ArgumentOutOfRangeException>(() => WorkerImageToolchain.Create(imageId, DockerOnly, RecordedAt));
    }
}
