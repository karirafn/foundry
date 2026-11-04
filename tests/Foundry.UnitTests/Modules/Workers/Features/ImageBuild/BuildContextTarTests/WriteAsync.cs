using System.Formats.Tar;

using Foundry.Modules.Workers.Features.ImageBuild;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.BuildContextTarTests;

public sealed class WriteAsync : IDisposable
{
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead
        | UnixFileMode.OtherRead;

    private readonly string _contextDirectory = Directory.CreateTempSubdirectory("build-context-").FullName;

    public void Dispose() => Directory.Delete(_contextDirectory, recursive: true);

    [Fact]
    public async Task WhenContextHasDotPrefixedEntries_IncludesThemWithRelativePaths()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_contextDirectory, ".config", ".nested"));
        await WriteFileAsync(".config/dotnet-tools.json", "{}");
        await WriteFileAsync(".config/.nested/a.txt", "a");
        await WriteFileAsync(".dockerignore", "obj");
        await WriteFileAsync("Dockerfile", "FROM x");
        await using MemoryStream archive = new();

        // Act
        await BuildContextTar.WriteAsync(_contextDirectory, archive, TestContext.Current.CancellationToken);

        // Assert
        List<string> names = (await ReadEntriesAsync(archive))
            .Select(entry => entry.Name.TrimEnd('/'))
            .ToList();
        names.ShouldBe(
            [".config", ".config/.nested", ".config/.nested/a.txt", ".config/dotnet-tools.json", ".dockerignore", "Dockerfile"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task WhenFileIsExecutable_PreservesMode()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await WriteFileAsync("entrypoint.sh", "#!/bin/sh");
        File.SetUnixFileMode(Path.Combine(_contextDirectory, "entrypoint.sh"), ExecutableMode);
        await using MemoryStream archive = new();

        // Act
        await BuildContextTar.WriteAsync(_contextDirectory, archive, TestContext.Current.CancellationToken);

        // Assert
        TarEntry entry = (await ReadEntriesAsync(archive)).Single();
        entry.Mode.ShouldBe(ExecutableMode);
    }

    private async Task WriteFileAsync(string relativePath, string content)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_contextDirectory, relativePath),
            content,
            TestContext.Current.CancellationToken);
    }

    private static async Task<List<TarEntry>> ReadEntriesAsync(MemoryStream archive)
    {
        archive.Seek(0, SeekOrigin.Begin);
        List<TarEntry> entries = [];
        await using TarReader reader = new(archive, leaveOpen: true);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(cancellationToken: TestContext.Current.CancellationToken)) is not null)
        {
            entries.Add(entry);
        }

        return entries;
    }
}
