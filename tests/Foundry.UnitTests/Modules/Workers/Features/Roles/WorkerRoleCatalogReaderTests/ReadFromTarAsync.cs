using System.Formats.Tar;
using System.Text;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.UnitTests.Fakes.Workers;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.WorkerRoleCatalogReaderTests;

public sealed class ReadFromTarAsync
{
    [Fact]
    public async Task WhenReadingTheRepositoryBuildContextTar_CapturesTheSameCatalogAsTheFolderOnDisk()
    {
        // Arrange — the same tar bytes the startup image build sends to Docker.
        await using MemoryStream tar = new();
        await BuildContextTar.WriteAsync(RepositoryRoot.WorkersDirectory, tar, TestContext.Current.CancellationToken);
        tar.Seek(0, SeekOrigin.Begin);

        // Act
        Result<WorkerRoleCatalogContent> fromTar =
            await WorkerRoleCatalogReader.ReadFromTarAsync(tar, TestContext.Current.CancellationToken);

        // Assert
        WorkerRoleCatalogContent tarCatalog = fromTar.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        WorkerRoleCatalogContent diskCatalog = WorkerRoleCatalogReader.ReadFromDirectory(RepositoryRoot.WorkersDirectory)
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        WorkerRoleDefinition fromTarImplement = tarCatalog.Find(WorkerRole.Implement).ShouldNotBeNull();
        WorkerRoleDefinition fromDiskImplement = diskCatalog.Find(WorkerRole.Implement).ShouldNotBeNull();
        fromTarImplement.ShouldSatisfyAllConditions(
            () => fromTarImplement.SystemPromptTemplate.ShouldBe(fromDiskImplement.SystemPromptTemplate),
            () => fromTarImplement.TaskPromptTemplate.ShouldBe(fromDiskImplement.TaskPromptTemplate),
            () => fromTarImplement.Plugin.Description.ShouldBe(fromDiskImplement.Plugin.Description));
    }

    [Fact]
    public async Task WhenRead_LeavesTheStreamOpenForTheImageBuild()
    {
        // Arrange
        await using MemoryStream tar = new();
        await BuildContextTar.WriteAsync(RepositoryRoot.WorkersDirectory, tar, TestContext.Current.CancellationToken);
        tar.Seek(0, SeekOrigin.Begin);

        // Act
        await WorkerRoleCatalogReader.ReadFromTarAsync(tar, TestContext.Current.CancellationToken);

        // Assert
        tar.CanSeek.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenARoleFileExceedsTheSizeCap_Fails()
    {
        // Arrange
        await using MemoryStream tar = new();
        await using (TarWriter writer = new(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            PaxTarEntry entry = new(TarEntryType.RegularFile, "roles/implement/prompts/system.md")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', WorkerRoleCatalogReader.MaxFileBytes + 1))),
            };
            await writer.WriteEntryAsync(entry, TestContext.Current.CancellationToken);
        }

        tar.Seek(0, SeekOrigin.Begin);

        // Act
        Result<WorkerRoleCatalogContent> result =
            await WorkerRoleCatalogReader.ReadFromTarAsync(tar, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<WorkerRoleCatalogContent>.Failure>().Error.Message.ShouldContain("larger than");
    }
}
