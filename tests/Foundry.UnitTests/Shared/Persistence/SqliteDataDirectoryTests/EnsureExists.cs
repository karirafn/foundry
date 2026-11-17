using Foundry.WebApi.Persistence;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Persistence.SqliteDataDirectoryTests;

public sealed class EnsureExists : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"foundry-sqlite-dir-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void WhenTheParentDirectoryIsMissing_CreatesIt()
    {
        // Arrange
        string dataDir = Path.Combine(_root, "nested", "data");

        // Act
        SqliteDataDirectory.EnsureExists($"Data Source={Path.Combine(dataDir, "foundry.db")}");

        // Assert
        Directory.Exists(dataDir).ShouldBeTrue();
    }

    [Fact]
    public void WhenTheParentDirectoryAlreadyExists_LeavesItInPlace()
    {
        // Arrange
        Directory.CreateDirectory(_root);
        string existingFile = Path.Combine(_root, "keep.txt");
        File.WriteAllText(existingFile, "x");

        // Act
        SqliteDataDirectory.EnsureExists($"Data Source={Path.Combine(_root, "foundry.db")}");

        // Assert
        File.Exists(existingFile).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=foundry;Mode=Memory;Cache=Shared")]
    public void WhenTheDatabaseIsInMemory_DoesNothing(string connectionString)
    {
        // Act / Assert
        Should.NotThrow(() => SqliteDataDirectory.EnsureExists(connectionString));
    }
}
