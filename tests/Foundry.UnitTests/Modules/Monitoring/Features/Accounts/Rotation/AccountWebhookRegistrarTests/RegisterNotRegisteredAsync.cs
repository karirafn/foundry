using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Rotation.AccountWebhookRegistrarTests;

public sealed class RegisterNotRegisteredAsync : IAsyncDisposable
{
    private const string ServerToken = "github_pat_server_side";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public RegisterNotRegisteredAsync()
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

    private AccountWebhookRegistrar BuildSut(RecordingBranchRulesProviderFactory factory) =>
        new(
            _dbContext,
            new RepositoryEligibilityDiffer(_dbContext, new UnusedEvaluator()),
            new WebhookRegistrar(
                new WebhookIngress(
                    Options.Create(new WebhookIngressOptions { Port = 5099 }),
                    new StubWebhookSettingsQueries("https://hooks.example.com")),
                factory,
                NullLogger<WebhookRegistrar>.Instance),
            NullLogger<AccountWebhookRegistrar>.Instance);

    private async Task<GitHubCredential> SeedCredentialAsync()
    {
        GitHubCredential credential = GitHubCredential.Create(
            "alice",
            "ghp_account",
            BaseUrl.Create("https://github.com").ValueOrThrow(),
            ServerToken);
        credential.SetNamespaces([Namespace.Create("alice").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential;
    }

    private async Task<MonitoredRepository> SeedRepoAsync(string slug, bool registered = false)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "github.com",
            pollInterval: null,
            position: await _dbContext.Set<MonitoredRepository>().CountAsync(CancellationToken.None)).ValueOrThrow();

        if (registered)
        {
            repo.MarkWebhookRegistered();
        }

        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return repo;
    }

    [Fact]
    public async Task RegistersOnlyTheAccountsNotRegisteredRepositoriesWithTheServerToken()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        MonitoredRepository unregistered = await SeedRepoAsync("alice/one");
        await SeedRepoAsync("alice/already", registered: true);
        await SeedRepoAsync("bob/other");
        RecordingBranchRulesProviderFactory factory = new();
        AccountWebhookRegistrar sut = BuildSut(factory);

        // Act
        await sut.RegisterNotRegisteredAsync(credential, CancellationToken.None);

        // Assert
        factory.WebhookUpserts.Select(u => u.Slug).ShouldBe(["alice/one"]);
        factory.TokensUsed.ShouldBe([ServerToken]);
        unregistered.WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    [Fact]
    public async Task WhenOneRepositoryThrows_TheOthersAreStillRegistered()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync();
        await SeedRepoAsync("alice/broken");
        MonitoredRepository healthy = await SeedRepoAsync("alice/healthy");
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithException("alice/broken", new InvalidOperationException("boom"));
        AccountWebhookRegistrar sut = BuildSut(factory);

        // Act
        await sut.RegisterNotRegisteredAsync(credential, CancellationToken.None);

        // Assert
        factory.WebhookUpserts.Select(u => u.Slug).ShouldBe(["alice/broken", "alice/healthy"], ignoreOrder: true);
        healthy.WebhookStatus.ShouldBe(WebhookStatus.Pending);
    }

    private sealed class UnusedEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(MonitoredRepository repo, DateTimeOffset now, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task EvaluateBranchRulesAndStoreAsync(MonitoredRepository repo, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
