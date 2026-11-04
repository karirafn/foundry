using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.Versioning;

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
    [UnsupportedOSPlatform("windows")]
    public async Task WhenFileIsExecutable_PreservesMode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes are not available on Windows.");

        // Arrange
        await WriteFileAsync("entrypoint.sh", "#!/bin/sh");
        File.SetUnixFileMode(Path.Combine(_contextDirectory, "entrypoint.sh"), ExecutableMode);
        await using MemoryStream archive = new();

        // Act
        await BuildContextTar.WriteAsync(_contextDirectory, archive, TestContext.Current.CancellationToken);

        // Assert
        TarEntry entry = (await ReadEntriesAsync(archive)).Single();
        entry.Mode.ShouldBe(ExecutableMode);
    }

    [Fact]
    public async Task WhenContextContainsSymlinkedFile_ThrowsInvalidOperationException()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires elevated rights on Windows.");

        // Arrange
        await WriteFileAsync("target.txt", "secret");
        File.CreateSymbolicLink(Path.Combine(_contextDirectory, "link.txt"), "target.txt");
        await using MemoryStream archive = new();

        // Act
        Func<Task> act = async () => await BuildContextTar.WriteAsync(
            _contextDirectory,
            archive,
            TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task WhenContextContainsSymlinkedDirectory_ThrowsInvalidOperationException()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links requires elevated rights on Windows.");

        // Arrange
        Directory.CreateDirectory(Path.Combine(_contextDirectory, "real"));
        Directory.CreateSymbolicLink(Path.Combine(_contextDirectory, "linked"), "real");
        await using MemoryStream archive = new();

        // Act
        Func<Task> act = async () => await BuildContextTar.WriteAsync(
            _contextDirectory,
            archive,
            TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task WhenContextContainsFifo_ThrowsInvalidOperationExceptionNamingRelativePath()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Creating a FIFO relies on mkfifo.");

        // Arrange
        Directory.CreateDirectory(Path.Combine(_contextDirectory, "dir"));
        string fifoPath = Path.Combine(_contextDirectory, "dir", "pipe");
        using (Process mkfifo = Process.Start("mkfifo", fifoPath)!)
        {
            await mkfifo.WaitForExitAsync(TestContext.Current.CancellationToken);
            mkfifo.ExitCode.ShouldBe(0);
        }

        await using MemoryStream archive = new();

        // Act
        Func<Task> act = async () => await BuildContextTar.WriteAsync(
            _contextDirectory,
            archive,
            TestContext.Current.CancellationToken);

        // Assert
        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(act);
        exception.Message.ShouldContain("dir/pipe");
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
