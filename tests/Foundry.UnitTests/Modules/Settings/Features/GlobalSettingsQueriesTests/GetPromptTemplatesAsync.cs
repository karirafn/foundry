using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.GlobalSettingsQueriesTests;

public sealed class GetPromptTemplatesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetPromptTemplatesAsync()
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
    public async Task WhenSettingsHaveTemplates_ReturnsBothTemplates()
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
        (string? systemPromptTemplate, string? workerPromptTemplate) = await sut.GetPromptTemplatesAsync(
            TestContext.Current.CancellationToken);

        // Assert
        systemPromptTemplate.ShouldBe("system prompt {issueNumber}");
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
