using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Webhooks.StatusBroadcast;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.StatusBroadcast.WebhookStatusBroadcastInterceptorTests;

internal sealed class InterceptorHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public InterceptorHarness()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using (FoundryDbContext seedContext = new(new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options))
        {
            seedContext.Database.EnsureCreated();
        }

        WebhookStatusBroadcastInterceptor interceptor = new(Broadcaster, Logger);

        DbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options);
    }

    public RecordingRepositoryWebhookBroadcaster Broadcaster { get; } = new();

    public CapturingLogger<WebhookStatusBroadcastInterceptor> Logger { get; } = new();

    public FoundryDbContext DbContext { get; }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>Seeds a repository with no broadcasts recorded for the seeding itself.</summary>
    public MonitoredRepository Seed(string slug, WebhookStatus status)
    {
        MonitoredRepository repository = new MonitoredRepositoryBuilder()
            .WithSlug(RepositorySlug.Create(slug).ValueOrThrow())
            .WithPosition(DbContext.Set<MonitoredRepository>().Count())
            .Build();

        if (status is not WebhookStatus.NotRegistered)
        {
            repository.MarkWebhookRegistered("stamp");
        }

        if (status is WebhookStatus.Active)
        {
            repository.RecordWebhookDelivery();
        }

        DbContext.Set<MonitoredRepository>().Add(repository);
        DbContext.SaveChanges();
        return repository;
    }
}
