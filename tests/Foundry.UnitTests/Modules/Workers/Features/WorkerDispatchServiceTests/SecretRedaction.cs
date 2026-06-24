using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class SecretRedaction : WorkerDispatchServiceTestBase
{
    [Fact]
    public async Task WhenContainerOutputContainsHttpsUrlWithUserinfo_UserinfoRedactedBeforeStore()
    {
        // Arrange
        SeedActiveRun("container-secret-url");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        string secretOutput = "Cloning into '/workspace'...\nhttps://glpat-MySecretToken@gitlab.example.com/owner/repo.git";
        FixedLogsStub orchestrator = new(status: failedStatus, logs: secretOutput);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        string output1 = failedRun.ContainerOutput.ShouldNotBeNull();
        output1.ShouldNotContain("glpat-MySecretToken");
        output1.ShouldContain("https://***@gitlab.example.com");
    }

    [Fact]
    public async Task WhenContainerOutputContainsTokenShape_TokenRedactedBeforeStore()
    {
        // Arrange
        SeedActiveRun("container-secret-token");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        string secretOutput = "error: Authentication failed for token ghp_abc123DefXyz";
        FixedLogsStub orchestrator = new(status: failedStatus, logs: secretOutput);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        string output2 = failedRun.ContainerOutput.ShouldNotBeNull();
        output2.ShouldNotContain("ghp_abc123DefXyz");
        output2.ShouldContain("***");
    }

    [Fact]
    public async Task WhenContainerOutputContainsSecretNearTruncationBoundary_SecretIsRedactedBeforeTruncation()
    {
        // Arrange — put secret at position 65500 (near the 65536 boundary)
        // so that if redaction happened AFTER truncation, the secret would be at position
        // 65500 and still present; but redaction before truncation must have already masked it.
        SeedActiveRun("container-secret-boundary");
        WorkerStatus failedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        string prefix = new('a', 65_500);
        string secretOutput = prefix + " ghp_SecretNearBoundary extra padding to exceed 65536";
        FixedLogsStub orchestrator = new(status: failedStatus, logs: secretOutput);
        WorkerDispatchService sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        string output3 = failedRun.ContainerOutput.ShouldNotBeNull();
        output3.ShouldNotContain("ghp_SecretNearBoundary");
    }

    private sealed class FixedLogsStub(WorkerStatus? status, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in redaction tests")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult(status);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult(logs);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
