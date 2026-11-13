using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publisher.PublisherContainerSpecTests;

public sealed class ForPublish
{
    private static readonly CommitSha Commit = CommitSha.From("3333333333333333333333333333333333333333");

    [Fact]
    public void AConfirmedRun_SetsOneGitleaksFingerprintPerConfirmedFinding()
    {
        // Arrange
        PublishRequest request = Request(
        [
            new SecretFinding("src/store.ts", 3, Commit, "generic-api-key"),
            new SecretFinding("tests/my fixtures/a:b.ts", 41, Commit, "heroku-api-key"),
        ]);

        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForPublish(request);

        // Assert
        spec.EnvironmentVariables["CONFIRMED_FINDINGS"].ShouldBe(
            $"{Commit.Value}:src/store.ts:generic-api-key:3\n{Commit.Value}:tests/my fixtures/a:b.ts:heroku-api-key:41");
    }

    [Fact]
    public void ARunWithNoConfirmedFindings_LeavesTheVariableUnset()
    {
        // Act
        WorkerContainerSpec spec = PublisherContainerSpec.ForPublish(Request([]));

        // Assert
        spec.ShouldSatisfyAllConditions(
            () => spec.EnvironmentVariables.ShouldNotContainKey("CONFIRMED_FINDINGS"),
            () => spec.Command.ShouldBe(["publish"]));
    }

    private static PublishRequest Request(IReadOnlyList<SecretFinding> confirmedFindings) =>
        new(
            WorkerRunId.New(),
            new Uri("https://github.com/owner/repo.git"),
            "ghp_token",
            BranchName.From("feat/42-add-retry"),
            new RunBase(CommitSha.From("1111111111111111111111111111111111111111"), RunBranchHead: null),
            new CommitIdentity("Foundry", "1+foundry@users.noreply.github.com"),
            new WorkerResourceLimitsSnapshot(2048, 2, 512),
            confirmedFindings);
}
