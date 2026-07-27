using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetPromptTemplatesAsync : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
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

    [Fact]
    public async Task WhenSettingsHaveTemplates_ReturnsSystemPromptTemplate()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdatePromptTemplates("system prompt {issueNumber}", "worker prompt {issueNumber}");
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        (string? systemPromptTemplate, string? _) = await sut.GetPromptTemplatesAsync(
            TestContext.Current.CancellationToken);

        // Assert
        systemPromptTemplate.ShouldBe("system prompt {issueNumber}");
    }

    [Fact]
    public async Task WhenSettingsHaveTemplates_ReturnsWorkerPromptTemplate()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.UpdatePromptTemplates("system prompt {issueNumber}", "worker prompt {issueNumber}");
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        (string? _, string? workerPromptTemplate) = await sut.GetPromptTemplatesAsync(
            TestContext.Current.CancellationToken);

        // Assert
        workerPromptTemplate.ShouldBe("worker prompt {issueNumber}");
    }

    [Fact]
    public async Task WhenSettingsHaveNoTemplates_ReturnsBothNull()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        (string? systemPromptTemplate, string? workerPromptTemplate) = await sut.GetPromptTemplatesAsync(
            TestContext.Current.CancellationToken);

        // Assert
        systemPromptTemplate.ShouldBeNull();
        workerPromptTemplate.ShouldBeNull();
    }

    [Fact]
    public async Task WhenNoSettingsExist_ReturnsBothNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        GlobalSettingsQueries sut = new(dbContext);

        // Act
        (string? systemPromptTemplate, string? workerPromptTemplate) = await sut.GetPromptTemplatesAsync(
            TestContext.Current.CancellationToken);

        // Assert
        systemPromptTemplate.ShouldBeNull();
        workerPromptTemplate.ShouldBeNull();
    }
}
