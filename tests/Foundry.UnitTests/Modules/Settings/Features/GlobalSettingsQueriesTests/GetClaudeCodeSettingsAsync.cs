using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetClaudeCodeSettingsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetClaudeCodeSettingsAsync()
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
            ClaudeCodeSettings claudeCode = new ClaudeCodeSettingsBuilder()
                .WithModel("claude-opus-4-1")
                .WithBlockCiCdEdits(false)
                .WithAdditionalDenyRules(["Bash(rm:*)", "Read(./secrets/**)"])
                .Build()
                .Match(v => v, e => throw new InvalidOperationException(e.Message));
            settings.UpdateClaudeCodeSettings(claudeCode);
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        ClaudeCodeSettingsSnapshot result = await sut.GetClaudeCodeSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Model.ShouldBe("claude-opus-4-1"),
            () => result.BlockCiCdEdits.ShouldBeFalse(),
            () => result.AdditionalDenyRules.ShouldBe(["Bash(rm:*)", "Read(./secrets/**)"]));
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsDefaultValues()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        ClaudeCodeSettingsSnapshot result = await sut.GetClaudeCodeSettingsAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.Model.ShouldBe(ClaudeCodeSettings.Default.Model),
            () => result.BlockCiCdEdits.ShouldBe(ClaudeCodeSettings.Default.BlockCiCdEdits),
            () => result.AdditionalDenyRules.ShouldBeEmpty());
    }
}
