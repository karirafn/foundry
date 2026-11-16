using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
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

public sealed class ProbeDueAsync : IAsyncDisposable
{
    private const string GroupSourceUrl = "https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json";
    private const string ThirdPartySourceUrl = "https://feed.example/v3/index.json";

    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly RecordingProber _prober = new();
    private readonly SlugCredentialResolver _resolver = new();

    public ProbeDueAsync()
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
        new(_dbContext, _resolver, _prober, NullLogger<PackageSourceProbing>.Instance);

    private static GitLabCredential Account(string name, string token = "unused-token") =>
        GitLabCredential.Create(name, token, BaseUrl.Create("https://gitlab.com").ValueOrThrow());

    private MonitoredRepository Repository(string slug, Credential account, params string[] sourceUrls)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "gitlab.com",
            pollInterval: null).ValueOrThrow();
        DeclaredPackageSource[] sources = sourceUrls
            .Select((url, index) => DeclaredPackageSource.Create($"source{index}", url).ValueOrThrow())
            .ToArray();
        repository.RecordRootListing(new RootNuGetConfig.Declared("sha", "<configuration />", sources), listingETag: null);
        _resolver.Accounts[slug] = account;
        return repository;
    }

    private async Task<PackageSource> StoredSourceAsync(string url)
    {
        await using FoundryDbContext context = NewContext();
        List<PackageSource> sources = await context.Set<PackageSource>().ToListAsync(TestContext.Current.CancellationToken);
        return sources.Single(s => s.ServiceIndexUrl.AbsoluteUri == url);
    }

    [Fact]
    public async Task WhenTheSourceIsOnTheRepositoryHost_ProbesWithTheAccountsUsernameAndToken()
    {
        // Arrange
        GitLabCredential account = Account("deploy-bot", token: "account-token");
        MonitoredRepository repository = Repository("group/app", account, GroupSourceUrl);

        // Act
        await CreateSut().ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Assert
        (Uri url, UpstreamCredential? credential) = _prober.Probes.ShouldHaveSingleItem();
        url.AbsoluteUri.ShouldBe(GroupSourceUrl);
        credential.ShouldNotBeNull().ShouldSatisfyAllConditions(
            c => c.Username.ShouldBe("deploy-bot"),
            c => c.Token.ShouldBe("account-token"));
    }

    [Fact]
    public async Task WhenTheSourceIsOnAnotherHost_ProbesWithoutACredential()
    {
        // Arrange
        MonitoredRepository repository = Repository("group/app", Account("deploy-bot"), ThirdPartySourceUrl);

        // Act
        await CreateSut().ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.ShouldHaveSingleItem().Credential.ShouldBeNull();
    }

    [Fact]
    public async Task RecordsTheVerdictUnderTheCredentialItWasProbedWith()
    {
        // Arrange
        GitLabCredential account = Account("deploy-bot");
        MonitoredRepository repository = Repository("group/app", account, GroupSourceUrl, ThirdPartySourceUrl);
        _prober.Verdicts[GroupSourceUrl] = new PackageSourceVerdict.Unreadable(401, "service index returned 401");

        // Act
        await CreateSut().ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Assert
        PackageSource group = await StoredSourceAsync(GroupSourceUrl);
        PackageSource thirdParty = await StoredSourceAsync(ThirdPartySourceUrl);
        group.ShouldSatisfyAllConditions(
            () => group.VerdictFor(new ProbeCredential.Account(account.Id))
                .ShouldBe(new PackageSourceVerdict.Unreadable(401, "service index returned 401")),
            () => thirdParty.VerdictFor(new ProbeCredential.Anonymous()).ShouldBeOfType<PackageSourceVerdict.Readable>(),
            () => group.Probes.ShouldHaveSingleItem().CheckedAt.ShouldBe(Now));
    }

    [Fact]
    public async Task WhenTwoRepositoriesOfOneAccountDeclareOneSource_ProbesItOnce()
    {
        // Arrange
        GitLabCredential account = Account("deploy-bot");
        MonitoredRepository first = Repository("group/app", account, GroupSourceUrl);
        MonitoredRepository second = Repository("group/lib", account, GroupSourceUrl);

        // Act
        await CreateSut().ProbeDueAsync([first, second], Now, TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenRepositoriesOfTwoAccountsDeclareOneSource_ProbesItOncePerAccount()
    {
        // Arrange
        MonitoredRepository first = Repository("group/app", Account("first-bot"), GroupSourceUrl);
        MonitoredRepository second = Repository("other/lib", Account("second-bot"), GroupSourceUrl);

        // Act
        await CreateSut().ProbeDueAsync([first, second], Now, TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.Select(p => p.Credential!.Username).ShouldBe(["first-bot", "second-bot"], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenAReadableVerdictIsWithinItsInterval_DoesNotProbeAgain()
    {
        // Arrange
        MonitoredRepository repository = Repository("group/app", Account("deploy-bot"), GroupSourceUrl);
        PackageSourceProbing sut = CreateSut();
        await sut.ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Act
        await sut.ProbeDueAsync([repository], Now + TimeSpan.FromMinutes(29), TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenAReadableVerdictIsThirtyMinutesOld_ProbesAgain()
    {
        // Arrange
        MonitoredRepository repository = Repository("group/app", Account("deploy-bot"), GroupSourceUrl);
        PackageSourceProbing sut = CreateSut();
        await sut.ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Act
        await sut.ProbeDueAsync([repository], Now + TimeSpan.FromMinutes(30), TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenAnUnreadableVerdictIsWithinItsCooldown_DoesNotProbeAgain()
    {
        // Arrange
        MonitoredRepository repository = Repository("group/app", Account("deploy-bot"), GroupSourceUrl);
        _prober.Verdicts[GroupSourceUrl] = new PackageSourceVerdict.Unreadable(401, "service index returned 401");
        PackageSourceProbing sut = CreateSut();
        await sut.ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Act
        await sut.ProbeDueAsync([repository], Now + TimeSpan.FromMinutes(14), TestContext.Current.CancellationToken);
        await sut.ProbeDueAsync([repository], Now + TimeSpan.FromMinutes(15), TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenTheAccountTokenIsReplaced_ProbesAtOnce()
    {
        // Arrange
        GitLabCredential account = Account("deploy-bot", token: "old-token");
        MonitoredRepository repository = Repository("group/app", account, GroupSourceUrl);
        PackageSourceProbing sut = CreateSut();
        await sut.ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);
        account.Update("deploy-bot", "new-token", account.BaseUrl);

        // Act
        await sut.ProbeDueAsync([repository], Now + TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.Select(p => p.Credential!.Token).ShouldBe(["old-token", "new-token"]);
    }

    [Fact]
    public async Task WhenTheRepositoryDeclaresANewSource_ProbesItAtOnce()
    {
        // Arrange
        GitLabCredential account = Account("deploy-bot");
        MonitoredRepository repository = Repository("group/app", account, GroupSourceUrl);
        PackageSourceProbing sut = CreateSut();
        await sut.ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);
        repository.RecordRootListing(
            new RootNuGetConfig.Declared(
                "sha-2",
                "<configuration />",
                [
                    DeclaredPackageSource.Create("gitlab", GroupSourceUrl).ValueOrThrow(),
                    DeclaredPackageSource.Create("feed", ThirdPartySourceUrl).ValueOrThrow(),
                ]),
            listingETag: null);

        // Act
        await sut.ProbeDueAsync([repository], Now + TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.Select(p => p.Url.AbsoluteUri).ShouldBe([GroupSourceUrl, ThirdPartySourceUrl]);
    }

    [Fact]
    public async Task WhenTheAccountTokenIsInvalid_DoesNotProbe()
    {
        // Arrange
        GitLabCredential account = Account("deploy-bot");
        account.MarkTokenInvalid(Now);
        MonitoredRepository repository = Repository("group/app", account, GroupSourceUrl);

        // Act
        await CreateSut().ProbeDueAsync([repository], Now, TestContext.Current.CancellationToken);

        // Assert
        _prober.Probes.ShouldBeEmpty();
    }

    internal sealed class RecordingProber : IPackageSourceProber
    {
        public Dictionary<string, PackageSourceVerdict.Probed> Verdicts { get; } = [];

        public List<(Uri Url, UpstreamCredential? Credential)> Probes { get; } = [];

        public Task<PackageSourceVerdict.Probed> ProbeAsync(
            Uri serviceIndexUrl,
            UpstreamCredential? credential,
            CancellationToken cancellationToken)
        {
            Probes.Add((serviceIndexUrl, credential));
            return Task.FromResult(Verdicts.TryGetValue(serviceIndexUrl.AbsoluteUri, out PackageSourceVerdict.Probed? verdict)
                ? verdict
                : new PackageSourceVerdict.Readable());
        }
    }

    internal sealed class SlugCredentialResolver : ICredentialResolver
    {
        public Dictionary<string, Credential> Accounts { get; } = [];

        public Task<Credential?> ResolveAsync(string host, RepositorySlug slug, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.GetValueOrDefault(slug.ToString()));
    }
}
