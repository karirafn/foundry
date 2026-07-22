using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxRelayServiceTests;

public sealed class TickAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecordingRelayEventHandler _handler;
    private readonly ServiceProvider _serviceProvider;

    public TickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _handler = new RecordingRelayEventHandler();

        _serviceProvider = BuildServiceProvider(_connection, _handler);

        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ServiceProvider BuildServiceProvider(
        SqliteConnection connection,
        RecordingRelayEventHandler handler,
        IIntegrationEventProcessor? processorOverride = null)
    {
        ServiceCollection services = new();

        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();

        if (processorOverride is not null)
        {
            services.AddScoped<IIntegrationEventProcessor>(_ => processorOverride);
        }
        else
        {
            services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();
        }

        services.AddScoped<IIntegrationEventHandler<TestRelayEvent>>(_ => handler);

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddSingleton(Options.Create(new OutboxOptions()));

        return services.BuildServiceProvider();
    }

    private OutboxRelayService CreateSut(ServiceProvider? provider = null)
    {
        ServiceProvider p = provider ?? _serviceProvider;
        IOptions<OutboxOptions> options = p.GetRequiredService<IOptions<OutboxOptions>>();
        IServiceScopeFactory scopeFactory = p.GetRequiredService<IServiceScopeFactory>();
        return new OutboxRelayService(scopeFactory, options, NullLogger<OutboxRelayService>.Instance);
    }

    private async Task SeedOutboxMessageAsync(IIntegrationEvent @event, DateTimeOffset occurredAt)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        OutboxMessage message = OutboxMessage.Create(@event, occurredAt);
        dbContext.Set<OutboxMessage>().Add(message);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenUnpublishedMessageExists_ProcessorInvokedAndRowMarkedPublished()
    {
        // Arrange
        TestRelayEvent @event = new("Hello");
        await SeedOutboxMessageAsync(@event, DateTimeOffset.UtcNow);

        OutboxRelayService sut = CreateSut();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        _handler.ReceivedEvents.Count.ShouldBe(1);
        _handler.ReceivedEvents[0].Name.ShouldBe("Hello");

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> messages = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        OutboxMessage published = messages.ShouldHaveSingleItem();
        published.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenMultipleMessagesSeededOutOfOrder_ProcessedInOccurredAtOrder()
    {
        // Arrange
        DateTimeOffset older = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset newer = DateTimeOffset.UtcNow;

        // Seed newer first, then older — relay must deliver in occurred-at order
        await SeedOutboxMessageAsync(new TestRelayEvent("Second"), newer);
        await SeedOutboxMessageAsync(new TestRelayEvent("First"), older);

        OutboxRelayService sut = CreateSut();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        _handler.ReceivedEvents.Count.ShouldBe(2);
        _handler.ReceivedEvents[0].Name.ShouldBe("First");
        _handler.ReceivedEvents[1].Name.ShouldBe("Second");
    }

    [Fact]
    public async Task WhenProcessorThrows_RowStaysUnpublishedAttemptsIncrementedAndNextMessageStillProcessed()
    {
        // Arrange
        DateTimeOffset older = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset newer = DateTimeOffset.UtcNow;

        await SeedOutboxMessageAsync(new TestRelayEvent("Poison"), older);
        await SeedOutboxMessageAsync(new TestRelayEvent("GoodEvent"), newer);

        // Build a provider with a processor that throws on first call, succeeds on subsequent
        ThrowOnFirstCallProcessor throwingProcessor = new(_handler);
        await using ServiceProvider throwingProvider = BuildServiceProvider(
            _connection,
            _handler,
            throwingProcessor);

        OutboxRelayService sut = CreateSut(throwingProvider);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — second message was still processed despite first throwing
        _handler.ReceivedEvents.Count.ShouldBe(1);
        _handler.ReceivedEvents[0].Name.ShouldBe("GoodEvent");

        // Assert — first row has failure recorded, not published; second row is published
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> messages = await dbContext.Set<OutboxMessage>()
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.Count.ShouldBe(2);

        OutboxMessage poison = messages[0];
        poison.ProcessedAt.ShouldBeNull();
        poison.Attempts.ShouldBe(1);
        poison.Error.ShouldNotBeNullOrEmpty();

        OutboxMessage good = messages[1];
        good.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenMessageAttemptsReachMaxAttempts_MessageIsSkipped()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OutboxOptions outboxOptions = new() { MaxAttempts = 3 };

        // Seed a message that already has MaxAttempts failures
        await using AsyncServiceScope seedScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext seedContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        OutboxMessage deadLettered = OutboxMessage.Create(new TestRelayEvent("DeadLetter"), now);
        // Simulate MaxAttempts failures
        deadLettered.RecordFailure("error1");
        deadLettered.RecordFailure("error2");
        deadLettered.RecordFailure("error3");
        seedContext.Set<OutboxMessage>().Add(deadLettered);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Build a provider with MaxAttempts=3
        await using ServiceProvider customProvider = BuildServiceProvider(_connection, _handler);
        customProvider.GetRequiredService<IOptions<OutboxOptions>>().Value.MaxAttempts = outboxOptions.MaxAttempts;

        IOptions<OutboxOptions> options = Options.Create(outboxOptions);
        IServiceScopeFactory scopeFactory = customProvider.GetRequiredService<IServiceScopeFactory>();
        OutboxRelayService sut = new(scopeFactory, options, NullLogger<OutboxRelayService>.Instance);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — processor never called; message remains unprocessed
        _handler.ReceivedEvents.ShouldBeEmpty();

        await using AsyncServiceScope verifyScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext verifyContext = verifyScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        OutboxMessage? msg = await verifyContext.Set<OutboxMessage>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        msg.ShouldNotBeNull();
        msg.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenMessageHasUnresolvableType_RecordFailureAndNoUnhandledThrow()
    {
        // Arrange — manually insert a message with a bad type name
        await using AsyncServiceScope seedScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext seedContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        // Create a valid message then set a bad type via the workaround of creating through reflection
        OutboxMessage badTypeMessage = OutboxMessage.Create(new TestRelayEvent("Dummy"), DateTimeOffset.UtcNow);
        seedContext.Set<OutboxMessage>().Add(badTypeMessage);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Update type column directly so EF doesn't know about the change
        await seedContext.Database.ExecuteSqlAsync(
            $"UPDATE outbox_messages SET type = 'NonExistent.Type.Assembly, BadAssembly' WHERE id = {badTypeMessage.Id}",
            TestContext.Current.CancellationToken);

        OutboxRelayService sut = CreateSut();

        // Act — must not throw
        await Should.NotThrowAsync(() => sut.TickForTest(TestContext.Current.CancellationToken));

        // Assert — row has failure recorded
        await using AsyncServiceScope verifyScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext verifyContext = verifyScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        OutboxMessage? msg = await verifyContext.Set<OutboxMessage>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        msg.ShouldNotBeNull();
        msg.ProcessedAt.ShouldBeNull();
        msg.Attempts.ShouldBe(1);
        msg.Error.ShouldNotBeNullOrEmpty();

        // Handler must not have been called
        _handler.ReceivedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMessagePayloadCannotBeDeserialized_RecordFailureAndNoUnhandledThrow()
    {
        // Arrange — insert a message with the correct type but invalid JSON payload
        await using AsyncServiceScope seedScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext seedContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        OutboxMessage validMessage = OutboxMessage.Create(new TestRelayEvent("Dummy"), DateTimeOffset.UtcNow);
        seedContext.Set<OutboxMessage>().Add(validMessage);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await seedContext.Database.ExecuteSqlAsync(
            $"UPDATE outbox_messages SET payload = '{{INVALID JSON}}' WHERE id = {validMessage.Id}",
            TestContext.Current.CancellationToken);

        OutboxRelayService sut = CreateSut();

        // Act — must not throw
        await Should.NotThrowAsync(() => sut.TickForTest(TestContext.Current.CancellationToken));

        // Assert — row has failure recorded
        await using AsyncServiceScope verifyScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext verifyContext = verifyScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        OutboxMessage? msg = await verifyContext.Set<OutboxMessage>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        msg.ShouldNotBeNull();
        msg.ProcessedAt.ShouldBeNull();
        msg.Attempts.ShouldBe(1);
        msg.Error.ShouldNotBeNullOrEmpty();

        _handler.ReceivedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMessageTypeResolvesButIsNotIIntegrationEvent_ErrorMentionsNonEventType()
    {
        // Arrange — insert a message whose type resolves but does not implement IIntegrationEvent
        await using AsyncServiceScope seedScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext seedContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        OutboxMessage validMessage = OutboxMessage.Create(new TestRelayEvent("Dummy"), DateTimeOffset.UtcNow);
        seedContext.Set<OutboxMessage>().Add(validMessage);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Overwrite the type column with a resolvable non-event type (System.String)
        string nonEventType = typeof(string).AssemblyQualifiedName!;
        await seedContext.Database.ExecuteSqlAsync(
            $"UPDATE outbox_messages SET type = {nonEventType} WHERE id = {validMessage.Id}",
            TestContext.Current.CancellationToken);

        OutboxRelayService sut = CreateSut();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — row is dead-lettered and error message identifies the non-event type
        await using AsyncServiceScope verifyScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext verifyContext = verifyScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        OutboxMessage? msg = await verifyContext.Set<OutboxMessage>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        msg.ShouldNotBeNull();
        msg.ProcessedAt.ShouldBeNull();
        msg.Attempts.ShouldBe(1);
        msg.Error.ShouldNotBeNull();
        msg.Error.ShouldContain("does not implement IIntegrationEvent");
    }

    /// <summary>
    /// A processor that throws <see cref="InvalidOperationException"/> on the first call,
    /// then delegates to the real handler on subsequent calls.
    /// </summary>
    private sealed class ThrowOnFirstCallProcessor(RecordingRelayEventHandler handler) : IIntegrationEventProcessor
    {
        private int _callCount;

        public async Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
        {
            int call = System.Threading.Interlocked.Increment(ref _callCount);

            if (call == 1)
            {
                throw new InvalidOperationException("Simulated processor failure.");
            }

            await handler.HandleAsync((TestRelayEvent)@event, cancellationToken);
        }
    }
}
