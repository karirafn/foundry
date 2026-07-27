using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.SettingsSeederTests;

public sealed class StartingAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public StartingAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
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

    private SettingsSeeder BuildSeeder()
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> dbOptions = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(dbOptions);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new SettingsSeeder(scopeFactory);
    }

    [Fact]
    public async Task WhenNoSettingsExist_CreatesDefaultGlobalSettings()
    {
        // Arrange
        SettingsSeeder sut = BuildSeeder();

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.Id.ShouldBe(GlobalSettingsId.Default);
    }

    [Fact]
    public async Task WhenSettingsAlreadyExist_DoesNotOverwriteExistingSettings()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings existing = GlobalSettings.Create();
            existing.UpdateLimits(5, 60);
            seedDb.Set<GlobalSettings>().Add(existing);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        SettingsSeeder sut = BuildSeeder();

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        int count = await assertDb.Set<GlobalSettings>().CountAsync(TestContext.Current.CancellationToken);
        GlobalSettings settings = (await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken))!;
        count.ShouldBe(1);
        settings.MaxConcurrent.ShouldBe(5);
        settings.TimeoutMinutes.ShouldBe(60);
    }
}
