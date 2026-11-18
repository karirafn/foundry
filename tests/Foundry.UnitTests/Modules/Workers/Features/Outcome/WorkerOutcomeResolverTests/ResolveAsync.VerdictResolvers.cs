using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeResolverTests;

public sealed partial class ResolveAsync
{
    private static ActiveRun CreateRunOfRole(WorkerRole role)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), role);
        return starting.Activate(ContainerId.From("container-restricted"), branchName: null, MonitoredRepositoryId.New());
    }

    [Fact]
    public async Task WhenARestrictedRunsRoleHasAVerdictResolver_ReturnsTheOutcomeThatResolverProduced()
    {
        // Arrange
        WorkerOutcome.Failure resolved = new(new FailureReason.ContainerError("from the role's resolver"), null, null);
        WorkerOutcomeResolver sut = new(
            new NoProviderCallsQueries(),
            new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance),
            [new FixedVerdictOutcomeResolver(WorkerRole.Triage, resolved)],
            prRetryDelay: TimeSpan.Zero);

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateRunOfRole(WorkerRole.Triage),
            Exited(0),
            ReadyOutput,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeSameAs(resolved);
    }

    [Fact]
    public async Task WhenARestrictedRunsStoredRoleIsUnknown_ReturnsAFailureNamingTheRole()
    {
        // Arrange
        WorkerOutcomeResolver sut = BuildTriageResolver();

        // Act
        WorkerOutcome outcome = await sut.ResolveAsync(
            CreateRunOfRole(WorkerRole.FromStored("retired-role")),
            Exited(0),
            ReadyOutput,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBeOfType<WorkerOutcome.Failure>()
            .FailureReason.ShouldBeOfType<FailureReason.ContainerError>()
            .Message.ShouldContain("retired-role");
    }

    [Fact]
    public async Task WhenARestrictedRoleInTheCatalogHasNoVerdictResolver_Throws()
    {
        // Arrange
        WorkerOutcomeResolver sut = new(
            new NoProviderCallsQueries(),
            new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance),
            verdictOutcomeResolvers: [],
            prRetryDelay: TimeSpan.Zero);

        // Act
        Func<Task> act = () => sut.ResolveAsync(
            CreateRunOfRole(WorkerRole.Triage),
            Exited(0),
            ReadyOutput,
            verdict: null,
            TestContext.Current.CancellationToken);

        // Assert
        (await act.ShouldThrowAsync<System.Diagnostics.UnreachableException>()).Message.ShouldContain(WorkerRole.Triage.Name);
    }

    private sealed class FixedVerdictOutcomeResolver(WorkerRole role, WorkerOutcome outcome) : IVerdictOutcomeResolver
    {
        public WorkerRole Role => role;

        public WorkerOutcome Resolve(IResolvableRun run, WorkerStatus? status, string? containerOutput) => outcome;
    }
}
