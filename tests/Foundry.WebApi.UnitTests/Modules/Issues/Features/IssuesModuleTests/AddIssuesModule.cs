using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Issues.Features;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Infrastructure;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class AddIssuesModule : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public AddIssuesModule()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();

        services.AddDbContext<FoundryDbContext>(opts =>
            opts.UseSqlite(_connection));

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddIssuesModule();

        _serviceProvider = services.BuildServiceProvider();

        using IServiceScope scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void WhenServicesRegistered_IIssuesModuleResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IIssuesModule module = scope.ServiceProvider.GetRequiredService<IIssuesModule>();
        module.ShouldBeOfType<IssuesModule>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueDetectedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueDetected> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueDetected>>();
        handler.ShouldBeOfType<CreateAndEnqueueIssueHandler>();
    }

    [Fact]
    public void WhenServicesRegistered_IssueDetailsChangedHandlerResolvable()
    {
        // Arrange & Act
        using IServiceScope scope = _serviceProvider.CreateScope();

        // Assert
        IDomainEventHandler<IssueDetailsChanged> handler =
            scope.ServiceProvider.GetRequiredService<IDomainEventHandler<IssueDetailsChanged>>();
        handler.ShouldBeOfType<UpdateIssueDetailsHandler>();
    }
}
