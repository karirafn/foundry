using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.UpdateWorkerEnvironmentTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private const int ValidMemoryLimitMb = 4096;
    private const double ValidCpuLimit = 4.0;
    private const int ValidPidsLimit = 1024;

    private readonly SqliteConnection _connection;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
    }

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();

        await using FoundryDbContext setup = CreateDbContext();
        await setup.Database.EnsureCreatedAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private async Task SeedSettingsAsync()
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static UpdateWorkerEnvironment.Command CreateCommand(
        int memoryLimitMb = ValidMemoryLimitMb,
        double cpuLimit = ValidCpuLimit,
        int pidsLimit = ValidPidsLimit,
        params UpdateWorkerEnvironment.MountRequest[] mounts) =>
        new(memoryLimitMb, cpuLimit, pidsLimit, mounts, string.Empty, []);

    private static UpdateWorkerEnvironment.Command CreateHostHooksCommand(
        string hostSettingsPath,
        params string[] excludedHookCommands) =>
        new(ValidMemoryLimitMb, ValidCpuLimit, ValidPidsLimit, [], hostSettingsPath, excludedHookCommands);

    [Fact]
    public async Task WhenValid_ReturnsSummaryWithNewLimitsAndMounts()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);
        UpdateWorkerEnvironment.Command command = CreateCommand(
            mounts: new UpdateWorkerEnvironment.MountRequest("/skills", "/home/user/skills", true));

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        GlobalSettingsSummary summary = result.Match(v => v, e => throw new InvalidOperationException(e.Message));
        summary.ShouldSatisfyAllConditions(
            () => summary.MemoryLimitMb.ShouldBe(ValidMemoryLimitMb),
            () => summary.CpuLimit.ShouldBe(ValidCpuLimit),
            () => summary.PidsLimit.ShouldBe(ValidPidsLimit),
            () => summary.Mounts.ShouldBe([new WorkerMountEntry("/skills", "/home/user/skills", true)]));
    }

    [Fact]
    public async Task WhenValid_PersistsLimitsAndMounts()
    {
        // Arrange
        await SeedSettingsAsync();
        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateWorkerEnvironment.Handler sut = new(dbContext);
            UpdateWorkerEnvironment.Command command = CreateCommand(
                mounts: new UpdateWorkerEnvironment.MountRequest("/cache", "/var/cache/foundry", false));

            // Act
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext verifyDb = CreateDbContext();
        GlobalSettings persisted = await verifyDb.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.ShouldSatisfyAllConditions(
            () => persisted.WorkerResourceLimits.MemoryLimitMb.ShouldBe(ValidMemoryLimitMb),
            () => persisted.WorkerMounts.Mounts.Single().ContainerPath.ShouldBe("/cache"));
    }

    [Fact]
    public async Task WhenSettingsDoNotExist_ReturnsNotFound()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            CreateCommand(),
            TestContext.Current.CancellationToken);

        // Assert
        Error error = ((Result<GlobalSettingsSummary>.Failure)result).Error;
        error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }

    [Theory]
    [InlineData(1, ValidCpuLimit, ValidPidsLimit, SettingsErrors.InvalidMemoryLimitCode)]
    [InlineData(ValidMemoryLimitMb, 0.0, ValidPidsLimit, SettingsErrors.InvalidCpuLimitCode)]
    [InlineData(ValidMemoryLimitMb, ValidCpuLimit, 1, SettingsErrors.InvalidPidsLimitCode)]
    public async Task WhenLimitsOutOfRange_ReturnsFieldError(
        int memoryLimitMb,
        double cpuLimit,
        int pidsLimit,
        string expectedCode)
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            CreateCommand(memoryLimitMb, cpuLimit, pidsLimit),
            TestContext.Current.CancellationToken);

        // Assert
        ((Result<GlobalSettingsSummary>.Failure)result).Error.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public async Task WhenMountElementIsNull_ReturnsMountError()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext db = CreateDbContext();
        UpdateWorkerEnvironment.Handler handler = new(db);

        // Act
        Result<GlobalSettingsSummary> result = await handler.HandleAsync(
            CreateCommand(mounts: [null!]),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
            .ShouldBe(SettingsErrors.MountContainerPathNotAbsoluteCode);
    }

    [Fact]
    public async Task WhenMountHostPathIsNull_ReturnsHostPathError()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext db = CreateDbContext();
        UpdateWorkerEnvironment.Handler handler = new(db);

        // Act
        Result<GlobalSettingsSummary> result = await handler.HandleAsync(
            CreateCommand(mounts: new UpdateWorkerEnvironment.MountRequest("/skills", null!, true)),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
            .ShouldBe(SettingsErrors.MountHostPathNotAbsoluteCode);
    }

    [Fact]
    public async Task WhenContainerPathNotAbsolute_ReturnsMountError()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);
        UpdateWorkerEnvironment.Command command = CreateCommand(
            mounts: new UpdateWorkerEnvironment.MountRequest("skills", "/home/user/skills", true));

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        ((Result<GlobalSettingsSummary>.Failure)result).Error.Code
            .ShouldBe(SettingsErrors.MountContainerPathNotAbsoluteCode);
    }

    [Fact]
    public async Task WhenContainerPathDuplicated_ReturnsDuplicateError()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);
        UpdateWorkerEnvironment.Command command = CreateCommand(
            mounts:
            [
                new UpdateWorkerEnvironment.MountRequest("/skills", "/home/user/a", true),
                new UpdateWorkerEnvironment.MountRequest("/skills", "/home/user/b", true),
            ]);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        ((Result<GlobalSettingsSummary>.Failure)result).Error.Code
            .ShouldBe(SettingsErrors.DuplicateMountContainerPathCode);
    }

    [Fact]
    public async Task WhenHostHooksValid_ReturnsSummaryAndPersistsCanonicalValues()
    {
        // Arrange
        await SeedSettingsAsync();
        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateWorkerEnvironment.Handler sut = new(dbContext);
            UpdateWorkerEnvironment.Command command = CreateHostHooksCommand(
                " /home/user/.claude/settings.json ",
                " notify ",
                "notify",
                "lint");

            // Act
            Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

            // Assert
            GlobalSettingsSummary summary = result.Match(v => v, e => throw new InvalidOperationException(e.Message));
            summary.ShouldSatisfyAllConditions(
                () => summary.HostSettingsPath.ShouldBe("/home/user/.claude/settings.json"),
                () => summary.ExcludedHookCommands.ShouldBe(["notify", "lint"]));
        }

        await using FoundryDbContext verifyDb = CreateDbContext();
        GlobalSettings persisted = await verifyDb.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.HostHooks.ShouldSatisfyAllConditions(
            () => persisted.HostHooks.HostSettingsPath.ShouldBe("/home/user/.claude/settings.json"),
            () => persisted.HostHooks.ExcludedCommands.ShouldBe(["notify", "lint"]));
    }

    [Fact]
    public async Task WhenHostSettingsPathIsRelative_ReturnsNotAbsoluteErrorAndPersistsNothing()
    {
        // Arrange
        await SeedSettingsAsync();
        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateWorkerEnvironment.Handler sut = new(dbContext);

            // Act
            Result<GlobalSettingsSummary> result = await sut.HandleAsync(
                CreateHostHooksCommand("settings.json"),
                TestContext.Current.CancellationToken);

            // Assert
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
                .ShouldBe(SettingsErrors.HostSettingsPathNotAbsoluteCode);
        }

        await using FoundryDbContext verifyDb = CreateDbContext();
        GlobalSettings persisted = await verifyDb.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.HostHooks.ShouldBe(HostHooks.Default);
    }

    [Fact]
    public async Task WhenExcludedHookCommandIsNull_ReturnsEmptyExclusionError()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateWorkerEnvironment.Handler sut = new(dbContext);
        UpdateWorkerEnvironment.Command command = CreateHostHooksCommand(string.Empty, [null!]);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
            .ShouldBe(SettingsErrors.EmptyExcludedHookCommandCode);
    }

    [Fact]
    public async Task WhenOnlyHostHooksChange_PersistsThem()
    {
        // Arrange
        await SeedSettingsAsync();
        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateWorkerEnvironment.Handler sut = new(dbContext);
            await sut.HandleAsync(CreateCommand(), TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateWorkerEnvironment.Handler sut = new(dbContext);

            // Act
            await sut.HandleAsync(
                CreateHostHooksCommand("/home/user/.claude/settings.json"),
                TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext verifyDb = CreateDbContext();
        GlobalSettings persisted = await verifyDb.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.HostHooks.HostSettingsPath.ShouldBe("/home/user/.claude/settings.json");
    }
}
