using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.UnclaimedRepositoryReclaimServiceTests;

public sealed class ReclaimAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly ScriptedNamespaceDeriver _deriver = new();
    private readonly CapturingLogger<UnclaimedRepositoryReclaimService> _logger = new();
    private int _nextPosition;

    public ReclaimAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddDbContext<FoundryDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<ICredentialResolver, CredentialResolver>();
        services.AddScoped<UnclaimedRepositories>();
        services.AddSingleton<INamespaceDeriver>(_deriver);
        services.AddScoped<IRepositoryEligibilityEvaluator, NullEligibilityEvaluator>();
        services.AddScoped<RepositoryEligibilityDiffer>();
        services.AddScoped<CredentialRotationService>();
        services.AddScoped<ClaimRecheck>();
        _services = services.BuildServiceProvider();

        using IServiceScope scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private UnclaimedRepositoryReclaimService BuildSut() =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _logger);

    private async Task SeedAsync(Action<FoundryDbContext> seed)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        seed(dbContext);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Task SeedRepositoryAsync(string host, string slug) =>
        SeedAsync(db => db.Set<MonitoredRepository>().Add(MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            host,
            null,
            _nextPosition++).ValueOrThrow()));

    private async Task<Guid> SeedGitHubAccountAsync(string baseUrl, string ns)
    {
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create(ns).ValueOrThrow(),
            new IssuedToken("github_pat_account", new TokenExpiry.NotChecked()),
            BaseUrl.Create(baseUrl).ValueOrThrow());
        credential.SetNamespaces([Namespace.Create(ns).ValueOrThrow()]);
        await SeedAsync(db => db.Set<Credential>().Add(credential));
        return credential.Id.Value;
    }

    [Fact]
    public async Task WhenNoRepositoryIsUnclaimed_MakesNoProviderCall()
    {
        // Arrange
        await SeedGitHubAccountAsync("https://github.com", "covered");
        await SeedRepositoryAsync("github.com", "covered/repo");

        // Act
        await BuildSut().ReclaimAsync(TestContext.Current.CancellationToken);

        // Assert
        _deriver.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RechecksOnlyHostsWithAnUnclaimedRepository()
    {
        // Arrange
        Guid coveredHostAccount = await SeedGitHubAccountAsync("https://github.com", "covered");
        await SeedRepositoryAsync("github.com", "covered/repo");
        Guid unclaimedHostAccount = await SeedGitHubAccountAsync("https://ghe.example.com", "team");
        await SeedRepositoryAsync("ghe.example.com", "orphan/repo");

        // Act
        await BuildSut().ReclaimAsync(TestContext.Current.CancellationToken);

        // Assert
        _deriver.Calls.ShouldBe([unclaimedHostAccount]);
        _deriver.Calls.ShouldNotContain(coveredHostAccount);
    }

    [Fact]
    public async Task WhenOneHostFails_LogsTheErrorAndRechecksTheRest()
    {
        // Arrange
        Guid failing = await SeedGitHubAccountAsync("https://ghe-a.example.com", "team-a");
        await SeedRepositoryAsync("ghe-a.example.com", "orphan/repo");
        Guid reclaiming = await SeedGitHubAccountAsync("https://ghe-b.example.com", "team-b");
        await SeedRepositoryAsync("ghe-b.example.com", "orphan/repo");
        _deriver.Failing.Add(failing);
        _deriver.Outcomes[reclaiming] = new NamespaceDerivationOutcome.Derived(
            [Namespace.Create("team-b").ValueOrThrow(), Namespace.Create("orphan").ValueOrThrow()],
            []);

        // Act
        await BuildSut().ReclaimAsync(TestContext.Current.CancellationToken);

        // Assert
        _deriver.Calls.ShouldBe([failing, reclaiming]);
        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.Message.Contains("ghe-a.example.com"));
        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("ghe-b.example.com claimed 1"));
    }

    private sealed class ScriptedNamespaceDeriver : INamespaceDeriver
    {
        public Dictionary<Guid, NamespaceDerivationOutcome> Outcomes { get; } = [];

        public HashSet<Guid> Failing { get; } = [];

        public List<Guid> Calls { get; } = [];

        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Credential credential,
            CancellationToken cancellationToken)
        {
            Calls.Add(credential.Id.Value);

            if (Failing.Contains(credential.Id.Value))
            {
                throw new InvalidOperationException("Provider unavailable.");
            }

            return Task.FromResult(
                Outcomes.TryGetValue(credential.Id.Value, out NamespaceDerivationOutcome? outcome)
                    ? outcome
                    : new NamespaceDerivationOutcome.Unavailable());
        }

        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Uri apiBaseUrl,
            string token,
            bool isGitLab,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NullEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
