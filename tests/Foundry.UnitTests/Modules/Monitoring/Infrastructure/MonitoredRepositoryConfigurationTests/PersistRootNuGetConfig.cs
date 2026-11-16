using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.MonitoredRepositoryConfigurationTests;

public sealed class PersistRootNuGetConfig : IAsyncDisposable
{
    private const string BlobSha = "44b4fc6d56897b048c772eb4087f854f46256132";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistRootNuGetConfig()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepository> SaveAndReloadAsync(MonitoredRepository repository)
    {
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        MonitoredRepository? reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .FindAsync([repository.Id], TestContext.Current.CancellationToken);

        return reloaded.ShouldNotBeNull();
    }

    private static MonitoredRepository NewRepository() =>
        MonitoredRepository.Create(RepositorySlug.Create("octocat/hello-world").ValueOrThrow(), "github.com", null)
            .ValueOrThrow();

    [Fact]
    public async Task WhenADeclaredConfigIsSaved_ReloadsItsSourcesTextShaAndETag()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        DeclaredPackageSource source = DeclaredPackageSource
            .Create("gitlab", "https://gitlab.com/api/v4/groups/1/-/packages/nuget/index.json")
            .ValueOrThrow();
        repository.RecordRootListing(
            new RootNuGetConfig.Declared(BlobSha, "<configuration />", [source]),
            "W/\"0123abcd\"");

        // Act
        MonitoredRepository loaded = await SaveAndReloadAsync(repository);

        // Assert
        RootNuGetConfig.Declared declared = loaded.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Declared>();
        loaded.ShouldSatisfyAllConditions(
            () => declared.Sources.ShouldBe([source]),
            () => declared.RawText.ShouldBe("<configuration />"),
            () => declared.BlobSha.ShouldBe(BlobSha),
            () => loaded.RootListingETag.ShouldBe("W/\"0123abcd\""));
    }

    [Fact]
    public async Task WhenAnInvalidConfigIsSaved_ReloadsItsMessage()
    {
        // Arrange
        MonitoredRepository repository = NewRepository();
        repository.RecordRootListing(new RootNuGetConfig.Invalid(BlobSha, "<broken", "Unexpected end of file."), null);

        // Act
        MonitoredRepository loaded = await SaveAndReloadAsync(repository);

        // Assert
        loaded.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Invalid>().Message.ShouldBe("Unexpected end of file.");
    }

    [Fact]
    public async Task WhenNothingWasRecorded_ReloadsWithNoSources()
    {
        // Act
        MonitoredRepository loaded = await SaveAndReloadAsync(NewRepository());

        // Assert
        loaded.ShouldSatisfyAllConditions(
            () => loaded.RootNuGetConfig.ShouldBeOfType<RootNuGetConfig.Absent>(),
            () => loaded.DeclaredPackageSources.ShouldBeEmpty());
    }
}
