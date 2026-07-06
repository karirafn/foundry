using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Features;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.ClaudeAccountSeederTests;

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

    private ClaudeAccountSeeder BuildSeeder()
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

        return new ClaudeAccountSeeder(scopeFactory);
    }

    [Fact]
    public async Task WhenNoAccountExists_CreatesDefaultClaudeAccount()
    {
        // Arrange
        ClaudeAccountSeeder sut = BuildSeeder();

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount? account = await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        account.ShouldNotBeNull();
        account.Id.ShouldBe(ClaudeAccountId.Default);
    }

    [Fact]
    public async Task WhenAccountAlreadyExists_DoesNotCreateDuplicate()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount existing = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(existing);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ClaudeAccountSeeder sut = BuildSeeder();

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        int count = await assertDb.Set<ClaudeAccount>().CountAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }
}
