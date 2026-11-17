using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
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

public sealed class ProbeSourceNowAsync : IAsyncDisposable
{
    private const string GroupSourceUrl = "https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json";
    private const string ThirdPartySourceUrl = "https://feed.example/v3/index.json";

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ProbeDueAsync.RecordingProber _prober = new();
    private readonly ProbeDueAsync.SlugCredentialResolver _resolver = new();
    private readonly CapturingSystemNotificationBroadcaster _broadcaster = new();
    private readonly CapturingDomainEventDispatcher _dispatcher = new();

    public ProbeSourceNowAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = NewContext();
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private FoundryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private PackageSourceProbing CreateSut() =>
        new(_dbContext, _resolver, _prober, new PackageSourceProbeGate(), new PackageSourceVerdictPublisher(_dispatcher, _broadcaster, NullLogger<PackageSourceVerdictPublisher>.Instance), NullLogger<PackageSourceProbing>.Instance);

    private static GitLabCredential Account(string name) =>
        GitLabCredential.Create(AccountName.Create(name).ValueOrThrow(), new IssuedToken("account-token", new TokenExpiry.NotChecked()), BaseUrl.Create("https://gitlab.com").ValueOrThrow());

    private async Task<MonitoredRepository> StoredRepositoryAsync(string slug, Credential account, params string[] sourceUrls)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "gitlab.com",
            pollInterval: null).ValueOrThrow();
        repository.SetPosition(_resolver.Accounts.Count);
        DeclaredPackageSource[] sources = sourceUrls
            .Select((url, index) => DeclaredPackageSource.Create($"source{index}", url).ValueOrThrow())
            .ToArray();
        repository.RecordRootListing(new RootNuGetConfig.Declared("sha", "<configuration />", sources), listingETag: null);
        _resolver.Accounts[slug] = account;
        _dbContext.Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repository;
    }

    private async Task<PackageSource> StoredSourceAsync(string url, Action<PackageSource>? change = null)
    {
        PackageSource source = PackageSource.Create(new Uri(url));
        change?.Invoke(source);
        _dbContext.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return source;
    }

    private async Task<PackageSource> ReloadAsync(PackageSourceId id)
    {
        await using FoundryDbContext context = NewContext();
        return await context.Set<PackageSource>().SingleAsync(s => s.Id == id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WithARegisteredCredential_ProbesOnceWithItForEveryDeclaringRepository()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", Account("first-bot"), ThirdPartySourceUrl);
        await StoredRepositoryAsync("other/lib", Account("second-bot"), ThirdPartySourceUrl);
        PackageSource source = await StoredSourceAsync(ThirdPartySourceUrl, s => s.SetCredential("ci-reader", "registered-token", Now));

        // Act
        await CreateSut().ProbeSourceNowAsync(source.Id, Now, TestContext.Current.CancellationToken);

        // Assert
        (Uri url, UpstreamCredential? credential) = _prober.Probes.ShouldHaveSingleItem();
        url.AbsoluteUri.ShouldBe(ThirdPartySourceUrl);
        credential.ShouldNotBeNull().ShouldSatisfyAllConditions(
            c => c.Username.ShouldBe("ci-reader"),
            c => c.Token.ShouldBe("registered-token"));
    }

    [Fact]
    public async Task WithARegisteredCredential_RecordsItsVerdict()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", Account("deploy-bot"), GroupSourceUrl);
        PackageSource source = await StoredSourceAsync(GroupSourceUrl, s => s.SetCredential("ci-reader", "registered-token", Now));
        _prober.Verdicts[GroupSourceUrl] = new PackageSourceVerdict.Unreadable(403, "search returned 403");

        // Act
        await CreateSut().ProbeSourceNowAsync(source.Id, Now, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(source.Id)).VerdictFor(new ProbeCredential.Registered())
            .ShouldBe(new PackageSourceVerdict.Unreadable(403, "search returned 403"));
    }

    [Fact]
    public async Task WithoutARegisteredCredential_ProbesWithEachRepositorysFallback()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", Account("first-bot"), GroupSourceUrl, ThirdPartySourceUrl);
        await StoredRepositoryAsync("other/lib", Account("second-bot"), GroupSourceUrl);
        PackageSource source = await StoredSourceAsync(GroupSourceUrl);

        // Act
        await CreateSut().ProbeSourceNowAsync(source.Id, Now, TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.ShouldAllBe(p => p.Url.AbsoluteUri == GroupSourceUrl);
        _prober.Probes.Select(p => p.Credential!.Username).ShouldBe(["first-bot", "second-bot"], ignoreOrder: true);
    }

    [Fact]
    public async Task SendsThePackageSourceNotificationAfterTheProbe()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", Account("deploy-bot"), GroupSourceUrl);
        PackageSource source = await StoredSourceAsync(GroupSourceUrl, s => s.SetCredential("ci-reader", "registered-token", Now));

        // Act
        await CreateSut().ProbeSourceNowAsync(source.Id, Now, TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.SentNotifications.ShouldHaveSingleItem()
            .ShouldBe(new SystemNotification(PackageSourceNotification.Category, IsActive: false, Message: string.Empty));
    }

    [Fact]
    public async Task WhenTheVerdictIsUnchanged_StillSendsTheNotification()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", Account("deploy-bot"), GroupSourceUrl);
        PackageSource source = await StoredSourceAsync(GroupSourceUrl, s => s.SetCredential("ci-reader", "registered-token", Now));
        PackageSourceProbing sut = CreateSut();
        await sut.ProbeSourceNowAsync(source.Id, Now, TestContext.Current.CancellationToken);

        // Act
        await sut.ProbeSourceNowAsync(source.Id, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.SentNotifications.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenNoVerdictChangesReadability_StillDispatchesTheReadabilityEventForTheSource()
    {
        // Arrange: a bad registered credential moves the verdict from Unchecked to Unreadable, which is no flip, but
        // the declaring repositories now read the source with it.
        await StoredRepositoryAsync("group/app", Account("deploy-bot"), GroupSourceUrl);
        PackageSource source = await StoredSourceAsync(GroupSourceUrl, s => s.SetCredential("ci-reader", "bad-token", Now));
        _prober.Verdicts[GroupSourceUrl] = new PackageSourceVerdict.Unreadable(401, "service index returned 401");

        // Act
        await CreateSut().ProbeSourceNowAsync(source.Id, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBe([new PackageSourceReadabilityChanged(source.Id, source.ServiceIndexUrl)]);
    }

    [Fact]
    public async Task WhenAVerdictChangesReadability_DispatchesTheReadabilityEventOnce()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", Account("deploy-bot"), GroupSourceUrl);
        PackageSource source = await StoredSourceAsync(GroupSourceUrl, s => s.SetCredential("ci-reader", "registered-token", Now));

        // Act
        await CreateSut().ProbeSourceNowAsync(source.Id, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBe([new PackageSourceReadabilityChanged(source.Id, source.ServiceIndexUrl)]);
    }

    [Fact]
    public async Task WhenTheSourceIsGone_DispatchesNothing()
    {
        // Act
        await CreateSut().ProbeSourceNowAsync(PackageSourceId.New(), Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }
}
