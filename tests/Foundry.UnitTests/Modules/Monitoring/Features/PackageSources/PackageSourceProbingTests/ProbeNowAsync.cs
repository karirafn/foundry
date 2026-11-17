using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.PackageSourceProbingTests;

public sealed class ProbeNowAsync : IAsyncDisposable
{
    private const string GroupSourceUrl = "https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json";

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public ProbeNowAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheVerdictIsNotDue_ProbesAnyway()
    {
        // Arrange
        ProbeDueAsync.RecordingProber prober = new();
        prober.Verdicts[GroupSourceUrl] = new PackageSourceVerdict.Unreadable(401, "service index returned 401");
        ProbeDueAsync.SlugCredentialResolver resolver = new();
        resolver.Accounts["group/app"] = GitLabCredential.Create(
            AccountName.Create("deploy-bot").ValueOrThrow(),
            new IssuedToken("token", new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("group/app").ValueOrThrow(),
            "gitlab.com",
            pollInterval: null).ValueOrThrow();
        repository.RecordRootListing(
            new RootNuGetConfig.Declared("sha", "<configuration />", [DeclaredPackageSource.Create("gitlab", GroupSourceUrl).ValueOrThrow()]),
            listingETag: null);
        PackageSourceProbing sut = new(
            _dbContext,
            resolver,
            prober,
            new PackageSourceProbeGate(),
            new PackageSourceVerdictPublisher(new NullDomainEventDispatcher(), new CapturingSystemNotificationBroadcaster(), NullLogger<PackageSourceVerdictPublisher>.Instance),
            NullLogger<PackageSourceProbing>.Instance);
        await sut.ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Act
        await sut.ProbeNowAsync(repository, Now + TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        prober.Probes.Count.ShouldBe(2);
    }
}
