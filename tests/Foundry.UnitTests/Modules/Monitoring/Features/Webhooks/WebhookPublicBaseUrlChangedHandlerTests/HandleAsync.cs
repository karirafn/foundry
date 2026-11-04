using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Settings.Contracts;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.WebhookPublicBaseUrlChangedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string OldBaseUrl = "https://old.example.com";
    private const string NewBaseUrl = "https://new.example.com";
    private const string ServerToken = "github_pat_server_side";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
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

    private WebhookPublicBaseUrlChangedHandler BuildSut(RecordingBranchRulesProviderFactory factory) =>
        new(
            _dbContext,
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            new WebhookRegistrar(
                new WebhookIngress(
                    Options.Create(new WebhookIngressOptions { Port = 5099 }),
                    new StubWebhookSettingsQueries(NewBaseUrl)),
                factory,
                NullLogger<WebhookRegistrar>.Instance),
            NullLogger<WebhookPublicBaseUrlChangedHandler>.Instance);

    private async Task SeedCredentialAsync()
    {
        GitHubCredential credential = GitHubCredential.Create(
            "alice",
            "ghp_account",
            BaseUrl.Create("https://github.com").ValueOrThrow(),
            ServerToken);
        credential.SetNamespaces([Namespace.Create("alice").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<MonitoredRepository> SeedRepoAsync(string slug, WebhookStatus status)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "github.com",
            pollInterval: null,
            position: await _dbContext.Set<MonitoredRepository>().CountAsync(CancellationToken.None)).ValueOrThrow();

        if (status != WebhookStatus.NotRegistered)
        {
            repo.MarkWebhookRegistered();
        }

        if (status is WebhookStatus.Active or WebhookStatus.Silent)
        {
            repo.RecordWebhookDelivery();
        }

        if (status == WebhookStatus.Silent)
        {
            repo.RecordMissedChange();
        }

        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return repo;
    }

    [Fact]
    public async Task RepointsEveryRegisteredHookAndSkipsNotRegisteredOnes()
    {
        // Arrange
        await SeedCredentialAsync();
        MonitoredRepository pending = await SeedRepoAsync("alice/pending", WebhookStatus.Pending);
        await SeedRepoAsync("alice/active", WebhookStatus.Active);
        await SeedRepoAsync("alice/silent", WebhookStatus.Silent);
        await SeedRepoAsync("alice/unregistered", WebhookStatus.NotRegistered);
        RecordingBranchRulesProviderFactory factory = new();
        WebhookPublicBaseUrlChangedHandler sut = BuildSut(factory);

        // Act
        await sut.HandleAsync(new WebhookPublicBaseUrlChanged(OldBaseUrl, NewBaseUrl), CancellationToken.None);

        // Assert — processed in position order, each matched against its previous payload URL.
        factory.WebhookUpserts.Select(u => u.Slug).ShouldBe(["alice/pending", "alice/active", "alice/silent"]);
        factory.TokensUsed.ShouldAllBe(t => t == ServerToken);
        factory.WebhookUpserts[0].Spec.ShouldSatisfyAllConditions(
            s => s.PayloadUrl.ShouldBe($"{NewBaseUrl}/webhooks/github/{pending.WebhookHookId}"),
            s => s.PreviousPayloadUrls.ShouldBe([$"{OldBaseUrl}/webhooks/github/{pending.WebhookHookId}"]));
    }

    [Fact]
    public async Task WhenOneRepositoryFails_TheRestAreStillProcessed()
    {
        // Arrange
        await SeedCredentialAsync();
        await SeedRepoAsync("alice/broken", WebhookStatus.Active);
        await SeedRepoAsync("alice/healthy", WebhookStatus.Silent);
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithException("alice/broken", new InvalidOperationException("boom"));
        WebhookPublicBaseUrlChangedHandler sut = BuildSut(factory);

        // Act
        await sut.HandleAsync(new WebhookPublicBaseUrlChanged(OldBaseUrl, NewBaseUrl), CancellationToken.None);

        // Assert — the healthy repository was re-registered and moved from Silent to Pending.
        factory.WebhookUpserts.Select(u => u.Slug).ShouldBe(["alice/broken", "alice/healthy"]);
        _dbContext.ChangeTracker.Clear();
        MonitoredRepository healthy = await _dbContext.Set<MonitoredRepository>()
            .SingleAsync(r => r.Slug == RepositorySlug.Create("alice/healthy").ValueOrThrow(), CancellationToken.None);
        healthy.WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }
}
