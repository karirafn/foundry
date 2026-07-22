using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventProcessorTests;

public sealed class ProcessAsync : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public ProcessAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task WhenHandlerRegistered_HandlerReceivesEvent()
    {
        // Arrange
        RecordingProcessorEventHandler handler = new();
        ServiceCollection services = new();
        services.AddSingleton<IIntegrationEventHandler<TestProcessorEvent>>(handler);
        IServiceProvider provider = services.BuildServiceProvider();

        IIntegrationEventProcessor sut = new IntegrationEventProcessor(provider, _dbContext);
        TestProcessorEvent @event = new("SomethingHappened");

        // Act
        await sut.ProcessAsync(Guid.NewGuid(), @event, CancellationToken.None);

        // Assert
        handler.ReceivedEvents.ShouldContain(@event);
    }

    [Fact]
    public async Task WhenMultipleHandlersRegisteredForSameEvent_AllHandlersReceiveEvent()
    {
        // Arrange — use distinct handler types so each has a unique FullName for dedup keying
        RecordingProcessorEventHandler handlerA = new();
        SecondRecordingProcessorEventHandler handlerB = new();
        ServiceCollection services = new();
        services.AddSingleton<IIntegrationEventHandler<TestProcessorEvent>>(handlerA);
        services.AddSingleton<IIntegrationEventHandler<TestProcessorEvent>>(handlerB);
        IServiceProvider provider = services.BuildServiceProvider();

        IIntegrationEventProcessor sut = new IntegrationEventProcessor(provider, _dbContext);
        TestProcessorEvent @event = new("SomethingHappened");

        // Act
        await sut.ProcessAsync(Guid.NewGuid(), @event, CancellationToken.None);

        // Assert
        handlerA.ReceivedEvents.ShouldContain(@event);
        handlerB.ReceivedEvents.ShouldContain(@event);
    }

    [Fact]
    public async Task WhenServiceProviderReturnsNullItemForHandlerType_IsSkippedWithoutThrowing()
    {
        // Arrange — a provider that returns an enumerable containing null, simulating a non-conforming DI registration
        IServiceProvider provider = new NullItemServiceProvider();

        IIntegrationEventProcessor sut = new IntegrationEventProcessor(provider, _dbContext);
        TestProcessorEvent @event = new("SomethingHappened");

        // Act
        Task act = sut.ProcessAsync(Guid.NewGuid(), @event, CancellationToken.None);

        // Assert
        await Should.NotThrowAsync(act);
    }

    private sealed class NullItemServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            // Return a single-null enumerable when asked for IEnumerable<IIntegrationEventHandler<T>>
            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return new object?[] { null };
            }

            return null;
        }
    }
}
