using System.Data.Common;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.RepositoryRemovalTests;

public sealed class OutboxHarvest : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly CommitRefusingInterceptor _commitRefuser = new();
    private readonly ServiceProvider _serviceProvider;

    public OutboxHarvest()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();
        services.AddScoped<IDomainEventHandler<MonitoredRepositoryRemovedDomainEvent>, MonitoredRepositoryRemovedBridge>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(_connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>(), _commitRefuser);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        _serviceProvider = services.BuildServiceProvider();

        using IServiceScope scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheRemovalCommits_CommitsExactlyOneRemovedOutboxRow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepositoryAsync();

        // Act
        await RemoveAsync(repositoryId);

        // Assert
        List<OutboxMessage> rows = await LoadOutboxAsync();
        rows.ShouldHaveSingleItem().Type.ShouldBe(typeof(MonitoredRepositoryRemoved).AssemblyQualifiedName);
    }

    [Fact]
    public async Task WhenTheRemovalCommits_TheOutboxRowCarriesTheRepositoryAndRemovalTime()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepositoryAsync();

        // Act
        await RemoveAsync(repositoryId);

        // Assert
        OutboxMessage row = (await LoadOutboxAsync()).ShouldHaveSingleItem();
        System.Text.Json.JsonSerializer
            .Deserialize<MonitoredRepositoryRemoved>(row.Payload, OutboxSerializerOptions.Default)
            .ShouldBe(new MonitoredRepositoryRemoved(repositoryId, Now));
    }

    [Fact]
    public async Task WhenTheRemovalRollsBack_CommitsNoOutboxRow()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepositoryAsync();
        _commitRefuser.Refuse = true;

        // Act
        await Should.ThrowAsync<InvalidOperationException>(() => RemoveAsync(repositoryId));

        // Assert
        _commitRefuser.Refuse = false;
        (await LoadOutboxAsync()).ShouldBeEmpty();
    }

    private async Task<MonitoredRepositoryId> SeedRepositoryAsync()
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("alice/repo").ValueOrThrow(),
            "github.com",
            null,
            0).ValueOrThrow();
        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repository.Id;
    }

    private async Task RemoveAsync(MonitoredRepositoryId repositoryId)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        MonitoredRepository repository = await dbContext.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == repositoryId, TestContext.Current.CancellationToken);
        RepositoryRemoval sut = new(
            dbContext,
            new RepositoryWebhookDeleter(
                new CredentialResolver(dbContext, NullLogger<CredentialResolver>.Instance),
                new RecordingBranchRulesProviderFactory(),
                NullLogger<RepositoryWebhookDeleter>.Instance),
            new Foundry.Modules.Issues.Features.RepositoryReactions.RepositoryRemovalBlockers(
                dbContext,
                new Foundry.Modules.Workers.Features.Runs.WorkerRunQueries(dbContext)),
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>());

        await sut.RemoveAsync([repository], Now, TestContext.Current.CancellationToken);
    }

    private async Task<List<OutboxMessage>> LoadOutboxAsync()
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FoundryDbContext>()
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CommitRefusingInterceptor : DbTransactionInterceptor
    {
        public bool Refuse { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Refuse)
            {
                throw new InvalidOperationException("Commit refused by the test.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
