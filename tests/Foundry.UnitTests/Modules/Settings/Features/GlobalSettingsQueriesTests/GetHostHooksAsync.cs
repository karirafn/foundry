using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetHostHooksAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetHostHooksAsync()
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

    [Fact]
    public async Task WhenSettingsExistWithCustomValues_ReturnsStoredValues()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            HostHooks hostHooks = HostHooks.Create("/home/user/.claude/settings.json", ["notify", "lint"])
                .Match(v => v, e => throw new InvalidOperationException(e.Message));
            settings.UpdateHostHooks(hostHooks);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        HostHooksSnapshot result = await sut.GetHostHooksAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.HostSettingsPath.ShouldBe("/home/user/.claude/settings.json"),
            () => result.ExcludedCommands.ShouldBe(["notify", "lint"]));
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsNoPathAndNoExclusions()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        HostHooksSnapshot result = await sut.GetHostHooksAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.HostSettingsPath.ShouldBeEmpty(),
            () => result.ExcludedCommands.ShouldBeEmpty());
    }
}
