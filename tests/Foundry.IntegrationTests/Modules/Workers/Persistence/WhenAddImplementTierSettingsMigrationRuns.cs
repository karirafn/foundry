using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Persistence;

/// <summary>
/// The AddImplementTierSettings migration moves the implement role's model to the demanding Tier Model and seeds the
/// routine one. Each test rolls the migration back, shapes <c>worker_role_settings</c> as it was before, and applies it
/// again. Runs locally against SQLite.
/// </summary>
public sealed class WhenAddImplementTierSettingsMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261125111237_AddTriagedSpecHashAndOpenWork";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tier-migration-{Guid.NewGuid():N}.db");
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly("Foundry.WebApi"))
            .Options;

        _dbContext = new FoundryDbContext(options, DataProtectionProvider.Create("migration-test"));

        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM worker_role_settings; INSERT INTO worker_role_settings (role, model) VALUES ('triage', 'opus'), ('review', 'opus');",
            TestContext.Current.CancellationToken);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task SetImplementModelAsync(string model) =>
        await _dbContext.Database.ExecuteSqlAsync(
            $"INSERT INTO worker_role_settings (role, model) VALUES ('implement', {model})",
            TestContext.Current.CancellationToken);

    private async Task<Dictionary<ImplementDemand, ImplementTierSettings>> TiersAsync() =>
        await _dbContext.Set<ImplementTierSettings>()
            .AsNoTracking()
            .ToDictionaryAsync(tier => tier.Demand, TestContext.Current.CancellationToken);

    private async Task<List<string>> RoleNamesAsync() =>
        [
            .. (await _dbContext.Set<WorkerRoleSettings>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
                .Select(settings => settings.Role.Name)
                .Order(StringComparer.Ordinal),
        ];

    [Fact]
    public async Task WithNoImplementRow_SeedsRoutineSonnetAndDemandingOpus()
    {
        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        Dictionary<ImplementDemand, ImplementTierSettings> tiers = await TiersAsync();
        tiers.Count.ShouldBe(2);
        tiers[ImplementDemand.Routine].ShouldSatisfyAllConditions(
            tier => tier.Model.ShouldBe("sonnet"),
            tier => tier.Effort.ShouldBeEmpty());
        tiers[ImplementDemand.Demanding].ShouldSatisfyAllConditions(
            tier => tier.Model.ShouldBe("opus"),
            tier => tier.Effort.ShouldBeEmpty());
    }

    [Fact]
    public async Task WithAnOperatorChosenImplementModel_TheDemandingTierKeepsIt()
    {
        // Arrange
        await SetImplementModelAsync("claude-opus-4-5-20251101");

        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        Dictionary<ImplementDemand, ImplementTierSettings> tiers = await TiersAsync();
        tiers[ImplementDemand.Demanding].Model.ShouldBe("claude-opus-4-5-20251101");
        tiers[ImplementDemand.Routine].Model.ShouldBe("sonnet");
    }

    [Fact]
    public async Task WithAnEmptyImplementModel_TheDemandingTierStaysEmpty()
    {
        // Arrange
        await SetImplementModelAsync(string.Empty);

        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        (await TiersAsync())[ImplementDemand.Demanding].Model.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheImplementRoleRowIsDeletedAndTheOtherRolesStay()
    {
        // Arrange
        await SetImplementModelAsync("opus");

        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        (await RoleNamesAsync()).ShouldBe(["review", "triage"]);
    }

    [Fact]
    public async Task RollingBack_RestoresTheImplementRowFromTheDemandingTier()
    {
        // Arrange
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE implement_tier_settings SET model = 'claude-opus-4-5-20251101' WHERE demand = 'demanding'",
            TestContext.Current.CancellationToken);

        // Act
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Assert
        List<string> implementModels = await _dbContext.Database
            .SqlQueryRaw<string>("SELECT model AS Value FROM worker_role_settings WHERE role = 'implement'")
            .ToListAsync(TestContext.Current.CancellationToken);
        implementModels.ShouldBe(["claude-opus-4-5-20251101"]);
    }
}
