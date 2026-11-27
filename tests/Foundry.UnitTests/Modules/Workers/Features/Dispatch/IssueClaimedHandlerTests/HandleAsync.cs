using System.Reflection;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using StarterStubs = Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerRunStarterTests.HandleAsync;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.IssueClaimedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly WorkerRunStartSignal _signal = new();

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

    private IssueClaimedHandler BuildHandler() =>
        new(
            _dbContext,
            new StarterStubs.StubGlobalSettingsQueries(),
            StubWorkerRoleCatalogStore.With(),
            _signal,
            NullLogger<IssueClaimedHandler>.Instance);

    private static IssueClaimed BuildEvent(WorkerRunId? workerRunId = null)
    {
        ClaimedIssueDispatch dispatch = new(
            IssueId.New(),
            workerRunId ?? WorkerRunId.New(),
            42,
            "Test Issue",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            BranchName.From("feat/42-test-issue"),
            MonitoredRepositoryId.New(),
            new WorkerProvider.GitHub(),
            new DispatchContext.Fresh("feat/42-test-issue"),
            "https://api.github.com/repos/owner/repo/issues/42",
            Demand: ImplementDemand.Routine);
        return new IssueClaimed(dispatch);
    }

    [Fact]
    public async Task WhenClaimed_CommitsAStartingRunCarryingTheClaim()
    {
        // Arrange
        IssueClaimed @event = BuildEvent();

        // Act
        await BuildHandler().HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        StartingRun run = (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<StartingRun>();
        run.ShouldSatisfyAllConditions(
            () => run.Claim.ShouldBe(@event.Dispatch),
            () => run.StartAttemptedAt.ShouldBeNull());
    }

    [Fact]
    public async Task WhenClaimed_RemovesTheReservationInTheSameSave()
    {
        // Arrange
        IssueClaimed @event = BuildEvent();
        _dbContext.Set<DispatchReservation>().Add(new DispatchReservationBuilder().WithWorkerRunId(@event.Dispatch.WorkerRunId).Build());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await BuildHandler().HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        _dbContext.ShouldSatisfyAllConditions(
            () => _dbContext.Set<DispatchReservation>().Any().ShouldBeFalse(),
            () => _dbContext.Set<StartingRun>().Any(run => run.Id == @event.Dispatch.WorkerRunId).ShouldBeTrue());
    }

    [Fact]
    public async Task WhenClaimed_SignalsTheStarter()
    {
        // Act
        await BuildHandler().HandleAsync(BuildEvent(), TestContext.Current.CancellationToken);

        // Assert
        _signal.Reader.TryRead(out bool _).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheRunAlreadyExists_SkipsWithoutSignalling()
    {
        // Arrange — a prior delivery committed its run
        IssueClaimed @event = BuildEvent();
        _dbContext.Set<WorkerRun>().Add(StartingRun.Begin(@event.Dispatch.IssueId, @event.Dispatch.WorkerRunId));
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        await BuildHandler().HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        StartingRun run = (await _dbContext.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<StartingRun>();
        run.ShouldSatisfyAllConditions(
            () => run.Claim.ShouldBeNull(),
            () => _signal.Reader.TryRead(out bool _).ShouldBeFalse());
    }

    [Fact]
    public async Task WhenClaimed_MakesNoCallThatStartsTheRun()
    {
        // Arrange — every collaborator of the start is available and records any call made to it
        IPublisher publisher = CallRecorder.Create<IPublisher>();
        IWorkerOrchestrator orchestrator = CallRecorder.Create<IWorkerOrchestrator>();
        IIssueContentQueries issueContent = CallRecorder.Create<IIssueContentQueries>();
        ITrackerContextQueries trackerContext = CallRecorder.Create<ITrackerContextQueries>();
        IPostExitProviderQueries providerQueries = CallRecorder.Create<IPostExitProviderQueries>();
        IRepositoryDispatchQueries dispatchQueries = CallRecorder.Create<IRepositoryDispatchQueries>();

        ServiceCollection services = new();
        services.AddSingleton<DbContext>(_dbContext);
        services.AddSingleton(publisher);
        services.AddSingleton(orchestrator);
        services.AddSingleton(issueContent);
        services.AddSingleton(trackerContext);
        services.AddSingleton(providerQueries);
        services.AddSingleton(dispatchQueries);
        services.AddSingleton<IGlobalSettingsQueries>(new StarterStubs.StubGlobalSettingsQueries());
        services.AddSingleton<IWorkerRoleCatalogStore>(StubWorkerRoleCatalogStore.With());
        services.AddSingleton(_signal);
        services.AddSingleton<ILogger<IssueClaimedHandler>>(NullLogger<IssueClaimedHandler>.Instance);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IssueClaimedHandler sut = ActivatorUtilities.CreateInstance<IssueClaimedHandler>(provider);

        // Act
        await sut.HandleAsync(BuildEvent(), TestContext.Current.CancellationToken);

        // Assert
        IEnumerable<string> calls = new object[] { publisher, orchestrator, issueContent, trackerContext, providerQueries, dispatchQueries }
            .SelectMany(CallRecorder.CallsOf);
        calls.ShouldBeEmpty();
    }

    /// <summary>A proxy for any interface that records each call and then fails it.</summary>
    public class CallRecorder : DispatchProxy
    {
        private readonly List<string> _calls = [];

        public static T Create<T>()
            where T : class => Create<T, CallRecorder>();

        public static IReadOnlyList<string> CallsOf(object proxy) => ((CallRecorder)proxy)._calls;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _calls.Add(targetMethod!.Name);
            throw new InvalidOperationException($"{targetMethod.Name} was called.");
        }
    }
}
