using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.MonitoringSeederTests;

public sealed class StartingAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public StartingAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Initialize the schema via a transient context
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

    private MonitoringSeeder BuildSeeder(MonitoringOptions options)
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

        return new MonitoringSeeder(scopeFactory, Options.Create(options), NullLogger<MonitoringSeeder>.Instance);
    }

    [Fact]
    public async Task WhenGitHubAccountConfigured_CreatesAccountInDatabase()
    {
        // Arrange
        MonitoringOptions options = new()
        {
            Accounts =
            [
                new AccountOption
                {
                    Name = "my-org",
                    Type = "GitHub",
                    BaseUrl = "https://github.com",
                    SecretKeyName = "GITHUB_TOKEN",
                },
            ],
        };
        MonitoringSeeder sut = BuildSeeder(options);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        List<Account> accounts = assertDb.Set<Account>().ToList();
        accounts.Count.ShouldBe(1);
        GitHubAccount account = accounts[0].ShouldBeOfType<GitHubAccount>();
        account.ShouldSatisfyAllConditions(
            () => account.Name.ShouldBe("my-org"),
            () => account.BaseUrl.ShouldBe(new Uri("https://github.com")));
    }

    [Fact]
    public async Task WhenRepositoryConfigured_CreatesMonitoredRepositoryWithResolvedAccountId()
    {
        // Arrange
        MonitoringOptions options = new()
        {
            Accounts =
            [
                new AccountOption
                {
                    Name = "my-org",
                    Type = "GitHub",
                    BaseUrl = "https://github.com",
                    SecretKeyName = "GITHUB_TOKEN",
                },
            ],
            Repositories =
            [
                new RepositoryOption
                {
                    Slug = "owner/repo",
                    AccountName = "my-org",
                    IsActive = true,
                },
            ],
        };
        MonitoringSeeder sut = BuildSeeder(options);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        List<MonitoredRepository> repos = assertDb.Set<MonitoredRepository>().ToList();
        repos.Count.ShouldBe(1);
        Account seedAccount = assertDb.Set<Account>().Single();
        repos[0].ShouldSatisfyAllConditions(
            () => repos[0].Slug.ToString().ShouldBe("owner/repo"),
            () => repos[0].AccountId.ShouldBe(seedAccount.Id),
            () => repos[0].IsActive.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenRunTwice_DoesNotCreateDuplicates()
    {
        // Arrange
        MonitoringOptions options = new()
        {
            Accounts =
            [
                new AccountOption
                {
                    Name = "my-org",
                    Type = "GitHub",
                    BaseUrl = "https://github.com",
                    SecretKeyName = "GITHUB_TOKEN",
                },
            ],
            Repositories =
            [
                new RepositoryOption
                {
                    Slug = "owner/repo",
                    AccountName = "my-org",
                },
            ],
        };
        MonitoringSeeder sut = BuildSeeder(options);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        int accountCount = assertDb.Set<Account>().Count();
        int repoCount = assertDb.Set<MonitoredRepository>().Count();
        accountCount.ShouldBe(1);
        repoCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenAccountAlreadyExists_SkipsCreatingDuplicate()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GitHubAccount existing = GitHubAccount.Create("my-org", "OLD_TOKEN", new Uri("https://github.com"));
            seedDb.Set<Account>().Add(existing);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        MonitoringOptions options = new()
        {
            Accounts =
            [
                new AccountOption
                {
                    Name = "my-org",
                    Type = "GitHub",
                    BaseUrl = "https://github.com",
                    SecretKeyName = "NEW_TOKEN",
                },
            ],
        };
        MonitoringSeeder sut = BuildSeeder(options);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        int accountCount = assertDb.Set<Account>().Count();
        accountCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRepositoryAlreadyExists_SkipsCreatingDuplicate()
    {
        // Arrange
        AccountId accountId;
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", new Uri("https://github.com"));
            seedDb.Set<Account>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
            accountId = account.Id;

            Result<RepositorySlug> slugResult = RepositorySlug.Create("owner/repo");
            RepositorySlug slug = ((Result<RepositorySlug>.Success)slugResult).Value;
            MonitoredRepository existing = MonitoredRepository.Create(slug, accountId, null);
            seedDb.Set<MonitoredRepository>().Add(existing);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        MonitoringOptions options = new()
        {
            Accounts =
            [
                new AccountOption
                {
                    Name = "my-org",
                    Type = "GitHub",
                    BaseUrl = "https://github.com",
                    SecretKeyName = "TOKEN",
                },
            ],
            Repositories =
            [
                new RepositoryOption
                {
                    Slug = "owner/repo",
                    AccountName = "my-org",
                },
            ],
        };
        MonitoringSeeder sut = BuildSeeder(options);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        int repoCount = assertDb.Set<MonitoredRepository>().Count();
        repoCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRepositoryHasPollInterval_SetsIntervalOnEntity()
    {
        // Arrange
        MonitoringOptions options = new()
        {
            Accounts =
            [
                new AccountOption
                {
                    Name = "my-org",
                    Type = "GitHub",
                    BaseUrl = "https://github.com",
                    SecretKeyName = "TOKEN",
                },
            ],
            Repositories =
            [
                new RepositoryOption
                {
                    Slug = "owner/repo",
                    AccountName = "my-org",
                    PollIntervalSeconds = 120,
                },
            ],
        };
        MonitoringSeeder sut = BuildSeeder(options);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository repo = assertDb.Set<MonitoredRepository>().Single();
        repo.PollInterval.ShouldBe(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task WhenNoOptionsConfigured_NoEntitiesCreated()
    {
        // Arrange
        MonitoringOptions options = new();
        MonitoringSeeder sut = BuildSeeder(options);

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        int accountCount = assertDb.Set<Account>().Count();
        int repoCount = assertDb.Set<MonitoredRepository>().Count();
        accountCount.ShouldBe(0);
        repoCount.ShouldBe(0);
    }
}
