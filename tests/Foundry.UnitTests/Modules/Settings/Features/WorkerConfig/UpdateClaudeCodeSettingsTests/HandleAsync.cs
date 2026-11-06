using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.UpdateClaudeCodeSettingsTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private const string ValidRule = "Bash(rm -rf:*)";

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

    private static UpdateClaudeCodeSettings.Command CreateCommand(
        bool blockCiCdEdits = false,
        params string[] additionalDenyRules) =>
        new(blockCiCdEdits, additionalDenyRules);

    [Fact]
    public async Task WhenValid_ReturnsSummaryWithNewToggleAndRules()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateClaudeCodeSettings.Handler sut = new(dbContext);
        UpdateClaudeCodeSettings.Command command = CreateCommand(additionalDenyRules: ValidRule);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        GlobalSettingsSummary summary = result.Match(v => v, e => throw new InvalidOperationException(e.Message));
        summary.ShouldSatisfyAllConditions(
            () => summary.BlockCiCdEdits.ShouldBeFalse(),
            () => summary.AdditionalDenyRules.ShouldBe([ValidRule]));
    }

    [Fact]
    public async Task WhenValid_PersistsSettings()
    {
        // Arrange
        await SeedSettingsAsync();
        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateClaudeCodeSettings.Handler sut = new(dbContext);

            // Act
            await sut.HandleAsync(
                CreateCommand(additionalDenyRules: ValidRule),
                TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext verifyDb = CreateDbContext();
        GlobalSettings persisted = await verifyDb.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.ShouldSatisfyAllConditions(
            () => persisted.ClaudeCodeSettings.BlockCiCdEdits.ShouldBeFalse(),
            () => persisted.ClaudeCodeSettings.AdditionalDenyRules.ShouldBe([ValidRule]));
    }

    [Fact]
    public async Task WhenSettingsDoNotExist_ReturnsNotFound()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateClaudeCodeSettings.Handler sut = new(dbContext);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            CreateCommand(),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
            .ShouldBe(SettingsErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenDenyRuleMalformed_ReturnsInvalidDenyRule()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateClaudeCodeSettings.Handler sut = new(dbContext);
        UpdateClaudeCodeSettings.Command command = CreateCommand(additionalDenyRules: "not-a-rule");

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
            .ShouldBe(SettingsErrors.InvalidDenyRuleCode);
    }

    [Fact]
    public async Task WhenDenyRuleIsNull_ReturnsInvalidDenyRule()
    {
        // Arrange
        await SeedSettingsAsync();
        await using FoundryDbContext dbContext = CreateDbContext();
        UpdateClaudeCodeSettings.Handler sut = new(dbContext);
        UpdateClaudeCodeSettings.Command command = CreateCommand(additionalDenyRules: [null!]);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>().Error.Code
            .ShouldBe(SettingsErrors.InvalidDenyRuleCode);
    }

    [Fact]
    public async Task WhenUnchanged_DoesNotSave()
    {
        // Arrange
        await SeedSettingsAsync();
        DateTimeOffset updatedAtBefore = await ReadUpdatedAtAsync();
        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            UpdateClaudeCodeSettings.Handler sut = new(dbContext);
            UpdateClaudeCodeSettings.Command command = CreateCommand(blockCiCdEdits: true);

            // Act
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        (await ReadUpdatedAtAsync()).ShouldBe(updatedAtBefore);
    }

    private async Task<DateTimeOffset> ReadUpdatedAtAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = await db.Set<GlobalSettings>()
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        return settings.UpdatedAt;
    }
}
