using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventProcessorTests;

/// <summary>
/// Tests for <see cref="IntegrationEventProcessor"/> inbox dedup behaviour (step 5).
/// Uses a real SQLite <see cref="FoundryDbContext"/> with schema created, so constraint
/// violations and row-persistence can be verified.
/// </summary>
public sealed class ProcessAsyncWithDedup : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public ProcessAsyncWithDedup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection);

        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---------------------------------------------------------------------------
    // Infrastructure helpers
    // ---------------------------------------------------------------------------

    private static ServiceProvider BuildServiceProvider(
        SqliteConnection connection,
        RecordingDedupEventHandler? handlerOverride = null)
    {
        RecordingDedupEventHandler handler = handlerOverride ?? new RecordingDedupEventHandler();

        ServiceCollection services = new();

        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();
        services.AddScoped<IIntegrationEventHandler<TestDedupEvent>>(_ => handler);

        return services.BuildServiceProvider();
    }

    // ---------------------------------------------------------------------------
    // Behavior 1: first delivery invokes handler and writes one processed_events row
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenFirstDelivery_HandlerInvokedAndProcessedEventRowWritten()
    {
        // Arrange
        RecordingDedupEventHandler handler = new();
        await using ServiceProvider provider = BuildServiceProvider(_connection, handler);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IIntegrationEventProcessor sut = scope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        Guid eventId = Guid.NewGuid();
        TestDedupEvent @event = new("First");

        // Act
        await sut.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);

        // Assert — handler was called
        handler.InvokeCount.ShouldBe(1);

        // Assert — exactly one processed_events row exists for the event id
        List<ProcessedEvent> rows = await dbContext.Set<ProcessedEvent>()
            .ToListAsync(TestContext.Current.CancellationToken);
        rows.Count.ShouldBe(1);
        rows[0].EventId.ShouldBe(eventId);
    }

    // ---------------------------------------------------------------------------
    // Behavior 2: redelivery of the same eventId + already-processed handler is a NO-OP
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenRedeliveredWithAlreadyProcessedHandler_HandlerNotInvokedAgain()
    {
        // Arrange
        RecordingDedupEventHandler handler = new();
        await using ServiceProvider provider = BuildServiceProvider(_connection, handler);
        Guid eventId = Guid.NewGuid();
        TestDedupEvent @event = new("Repeat");

        // First delivery
        await using (AsyncServiceScope firstScope = provider.CreateAsyncScope())
        {
            IIntegrationEventProcessor processor = firstScope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
            await processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);
        }

        handler.InvokeCount.ShouldBe(1);

        // Act — redelivery with same eventId
        await using (AsyncServiceScope secondScope = provider.CreateAsyncScope())
        {
            IIntegrationEventProcessor processor = secondScope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
            await processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);
        }

        // Assert — handler invoked only once across both deliveries
        handler.InvokeCount.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------
    // Behavior 3: two handlers for one event -> two processed_events rows;
    //             fully-processed event redelivered invokes neither
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenTwoHandlersAndFullyProcessedEventRedelivered_NeitherHandlerInvokedAgain()
    {
        // Arrange — register two handlers with distinct types so each has a unique dedup key
        RecordingDedupEventHandler handlerA = new();
        SecondRecordingDedupEventHandler handlerB = new();

        ServiceCollection services = new();
        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(_connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();
        services.AddScoped<IIntegrationEventHandler<TestDedupEvent>>(_ => handlerA);
        services.AddScoped<IIntegrationEventHandler<TestDedupEvent>>(_ => handlerB);

        await using ServiceProvider provider = services.BuildServiceProvider();

        Guid eventId = Guid.NewGuid();
        TestDedupEvent @event = new("MultiHandler");

        // First delivery
        await using (AsyncServiceScope firstScope = provider.CreateAsyncScope())
        {
            IIntegrationEventProcessor processor = firstScope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
            await processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);
        }

        handlerA.InvokeCount.ShouldBe(1);
        handlerB.InvokeCount.ShouldBe(1);

        // Verify two rows were written
        await using (AsyncServiceScope verifyScope = provider.CreateAsyncScope())
        {
            FoundryDbContext dbContext = verifyScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            List<ProcessedEvent> rows = await dbContext.Set<ProcessedEvent>()
                .ToListAsync(TestContext.Current.CancellationToken);
            rows.Count.ShouldBe(2);
        }

        // Act — redelivery
        await using (AsyncServiceScope secondScope = provider.CreateAsyncScope())
        {
            IIntegrationEventProcessor processor = secondScope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
            await processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);
        }

        // Assert — neither handler called a second time
        handlerA.InvokeCount.ShouldBe(1);
        handlerB.InvokeCount.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------
    // Behavior 4: partial fan-out — handler A succeeds, handler B throws;
    //             A has a row, B does not; redelivery invokes only B
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenHandlerASucceedsAndHandlerBThrows_AHasRowBDoesNot_RedeliveryOnlyInvokesB()
    {
        // Arrange — A succeeds, B always throws
        RecordingDedupEventHandler handlerA = new();
        ThrowingDedupEventHandler handlerB = new();

        ServiceCollection services = new();
        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(_connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();
        services.AddScoped<IIntegrationEventHandler<TestDedupEvent>>(_ => handlerA);
        services.AddScoped<IIntegrationEventHandler<TestDedupEvent>>(_ => handlerB);

        await using ServiceProvider provider = services.BuildServiceProvider();

        Guid eventId = Guid.NewGuid();
        TestDedupEvent @event = new("PartialFanOut");

        // Act — first delivery (should throw because B throws)
        await using (AsyncServiceScope firstScope = provider.CreateAsyncScope())
        {
            IIntegrationEventProcessor processor = firstScope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
            await Should.ThrowAsync<InvalidOperationException>(
                () => processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken));
        }

        // Assert — A ran and has a row; B did not commit a row
        await using (AsyncServiceScope verifyScope = provider.CreateAsyncScope())
        {
            FoundryDbContext dbContext = verifyScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            List<ProcessedEvent> rows = await dbContext.Set<ProcessedEvent>()
                .ToListAsync(TestContext.Current.CancellationToken);
            rows.Count.ShouldBe(1);
            rows[0].EventId.ShouldBe(eventId);
        }

        handlerA.InvokeCount.ShouldBe(1);
        handlerB.InvokeCount.ShouldBe(1);

        // Act — redelivery; B now succeeds
        handlerB.ShouldSucceedOnNextCall = true;

        await using (AsyncServiceScope redeliveryScope = provider.CreateAsyncScope())
        {
            IIntegrationEventProcessor processor = redeliveryScope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
            await processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken);
        }

        // Assert — A NOT called again; B was called once more (now total 2)
        handlerA.InvokeCount.ShouldBe(1);
        handlerB.InvokeCount.ShouldBe(2);

        // Assert — now two rows exist
        await using (AsyncServiceScope finalScope = provider.CreateAsyncScope())
        {
            FoundryDbContext dbContext = finalScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            List<ProcessedEvent> rows = await dbContext.Set<ProcessedEvent>()
                .ToListAsync(TestContext.Current.CancellationToken);
            rows.Count.ShouldBe(2);
        }
    }

    // ---------------------------------------------------------------------------
    // Behavior 5: duplicate-key race — pre-seeded row is treated as already-processed
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenProcessedEventRowAlreadyExists_DuplicateKeyRaceIsSwallowedAndTreatedAsProcessed()
    {
        // Arrange — pre-seed a processed_events row for the handler
        RecordingDedupEventHandler handler = new();
        await using ServiceProvider provider = BuildServiceProvider(_connection, handler);

        Guid eventId = Guid.NewGuid();
        TestDedupEvent @event = new("Race");

        string handlerName = typeof(RecordingDedupEventHandler).FullName!;

        // Pre-seed the row to simulate a race where another instance already committed it
        await using (AsyncServiceScope seedScope = provider.CreateAsyncScope())
        {
            FoundryDbContext dbContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            dbContext.Set<ProcessedEvent>().Add(ProcessedEvent.For(eventId, handlerName, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act — deliver the event; the processor will see the row already exists (pre-skip path)
        await using (AsyncServiceScope deliveryScope = provider.CreateAsyncScope())
        {
            IIntegrationEventProcessor processor = deliveryScope.ServiceProvider.GetRequiredService<IIntegrationEventProcessor>();
            await Should.NotThrowAsync(
                () => processor.ProcessAsync(eventId, @event, TestContext.Current.CancellationToken));
        }

        // Assert — handler was NOT invoked (row was pre-seeded, so skip path taken)
        handler.InvokeCount.ShouldBe(0);

        // Assert — still exactly one row
        await using (AsyncServiceScope verifyScope = provider.CreateAsyncScope())
        {
            FoundryDbContext dbContext = verifyScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            List<ProcessedEvent> rows = await dbContext.Set<ProcessedEvent>()
                .ToListAsync(TestContext.Current.CancellationToken);
            rows.Count.ShouldBe(1);
        }
    }
}
