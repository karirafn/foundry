using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerLogStreamTests;

public sealed class StreamAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public StreamAsync()
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

    private sealed class CapturingOrchestrator : IWorkerOrchestrator
    {
        private readonly IReadOnlyList<string> _lines;
        public string? LastContainerId { get; private set; }

        public CapturingOrchestrator(IReadOnlyList<string> lines)
        {
            _lines = lines;
        }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastContainerId = containerId;

            foreach (string line in _lines)
            {
                yield return line;
                await Task.Yield();
            }
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ContainerId>> StartLoginContainerAsync(
            LoginContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("fake-login-container")));

        public Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<AccountIdentity>> GetAuthStatusAsync(
            string containerId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<AccountIdentity>.Ok(new AccountIdentity("test@example.com", "Test Org", "pro")));

        public Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ContainerId>>([]);

        public Task SeedOnboardingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private async Task<ActiveRun> SeedActiveRunAsync(string containerId = "container-stream-test")
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From(containerId),
            BranchName.From("feat/1-stream"),
            MonitoredRepositoryId.New());

        _dbContext.Set<WorkerRun>().Add(active);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return active;
    }

    private async Task<FailedRun> SeedFailedRunAsync()
    {
        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-failed-stream"),
            BranchName.From("feat/2-failed-stream"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(new FailureReason.TimedOut());

        _dbContext.Set<WorkerRun>().Add(failed);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return failed;
    }

    [Fact]
    public async Task WhenRunIsActive_StreamsLinesFromOrchestrator()
    {
        // Arrange
        ActiveRun run = await SeedActiveRunAsync();
        IReadOnlyList<string> expectedLines = ["line one", "line two", "line three"];
        CapturingOrchestrator orchestrator = new(expectedLines);
        WorkerLogStream sut = new(_dbContext, orchestrator);

        // Act
        List<string> received = [];
        await foreach (string line in sut.StreamAsync(run.Id.Value, CancellationToken.None))
        {
            received.Add(line);
        }

        // Assert
        received.ShouldBe(expectedLines);
    }

    [Fact]
    public async Task WhenRunIsActive_NeverExposesContainerIdInStream()
    {
        // Arrange
        string containerId = "secret-container-id-12345";
        ActiveRun run = await SeedActiveRunAsync(containerId: containerId);
        CapturingOrchestrator orchestrator = new(["some log line"]);
        WorkerLogStream sut = new(_dbContext, orchestrator);

        // Act
        List<string> received = [];
        await foreach (string line in sut.StreamAsync(run.Id.Value, CancellationToken.None))
        {
            received.Add(line);
        }

        // Assert — the container ID must never appear in the streamed lines
        foreach (string line in received)
        {
            line.ShouldNotContain(containerId);
        }
    }

    [Fact]
    public async Task WhenRunIsNotActive_YieldsNothing()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync();
        CapturingOrchestrator orchestrator = new(["should not stream"]);
        WorkerLogStream sut = new(_dbContext, orchestrator);

        // Act
        List<string> received = [];
        await foreach (string line in sut.StreamAsync(run.Id.Value, CancellationToken.None))
        {
            received.Add(line);
        }

        // Assert
        received.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRunDoesNotExist_YieldsNothing()
    {
        // Arrange
        Guid unknownId = Guid.NewGuid();
        CapturingOrchestrator orchestrator = new(["should not stream"]);
        WorkerLogStream sut = new(_dbContext, orchestrator);

        // Act
        List<string> received = [];
        await foreach (string line in sut.StreamAsync(unknownId, CancellationToken.None))
        {
            received.Add(line);
        }

        // Assert
        received.ShouldBeEmpty();
    }
}
