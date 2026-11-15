using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageToolchainRecorderTests;

/// <summary>A recorder over an in-memory SQLite database, a scripted probe and a catalog store the test controls.</summary>
internal sealed class RecorderFixture : IAsyncDisposable
{
    private static readonly ImageBuildRetryPolicy FastRetryPolicy = new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(40));

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    public RecorderFixture(string? imageId, ImageBuildRetryPolicy? retryPolicy = null)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddScoped<DbContext>(_ => new FoundryDbContext(
            new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options));
        _services = services.BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<DbContext>().Database.EnsureCreated();
        }

        Catalog = StubWorkerRoleCatalogStore.With(imageId: imageId);
        Recorder = new WorkerImageToolchainRecorder(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Probe,
            Catalog,
            TimeProvider.System,
            NullLogger<WorkerImageToolchainRecorder>.Instance,
            retryPolicy ?? FastRetryPolicy);
    }

    public ScriptedToolchainProbe Probe { get; } = new();

    public StubWorkerRoleCatalogStore Catalog { get; }

    public WorkerImageToolchainRecorder Recorder { get; }

    public static Toolchain DockerOnly() =>
        Toolchain.TryCreate([ToolchainEntry.TryCreate("docker", "29.9.3")!])!;

    public void PointCatalogAt(string imageId) => Catalog.Publish(StubWorkerRoleCatalogStore.Catalog(imageId: imageId));

    public async Task StoreAsync(string imageId, Toolchain toolchain)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<WorkerImageToolchain>().Add(WorkerImageToolchain.Create(imageId, toolchain, DateTimeOffset.UnixEpoch));
        await dbContext.SaveChangesAsync();
    }

    public async Task<WorkerImageToolchain?> FindAsync(string imageId)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<WorkerImageToolchain>()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.ImageId == imageId);
    }

    /// <summary>Polls until <paramref name="condition"/> holds, failing after five seconds.</summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not hold within five seconds.");
            }

            await Task.Delay(10);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Recorder.StopAsync(CancellationToken.None);
        Recorder.Dispose();
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>Returns the scripted results in order, then repeats the last; records every image it was asked to probe.</summary>
internal sealed class ScriptedToolchainProbe : IWorkerImageToolchainProbe
{
    private readonly Queue<Toolchain?> _results = new();
    private readonly List<string> _probedImageIds = [];
    private Toolchain? _last;

    public IReadOnlyList<string> ProbedImageIds
    {
        get
        {
            lock (_probedImageIds)
            {
                return [.. _probedImageIds];
            }
        }
    }

    public ScriptedToolchainProbe Returning(params Toolchain?[] results)
    {
        foreach (Toolchain? result in results)
        {
            _results.Enqueue(result);
        }

        return this;
    }

    public Task<Toolchain?> ProbeAsync(string imageId, CancellationToken cancellationToken)
    {
        lock (_probedImageIds)
        {
            _probedImageIds.Add(imageId);
            if (_results.Count > 0)
            {
                _last = _results.Dequeue();
            }

            return Task.FromResult(_last);
        }
    }
}
