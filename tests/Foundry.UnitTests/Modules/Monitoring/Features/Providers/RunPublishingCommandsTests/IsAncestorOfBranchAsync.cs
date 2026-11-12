using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.RunPublishingCommandsTests;

public sealed class IsAncestorOfBranchAsync : RunPublishingCommandsTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenBothRefsExist_ReturnsTheProvidersAnswer(bool isAncestor)
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.Ancestry = Result<bool>.Ok(isAncestor);

        // Act
        Result<bool> result = await Sut.IsAncestorOfBranchAsync(
            repositoryId, "feat/42-x-run-7", "feat/42-x", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ValueOrThrow().ShouldBe(isAncestor),
            () => Provider.Calls.ShouldBe(["ancestor owner/repo feat/42-x-run-7 feat/42-x"]));
    }

    [Fact]
    public async Task WhenARefIsMissing_FailsNotFound()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = await SeedRepoAsync();
        Provider.Ancestry = Result<bool>.Fail(RunPublishingErrors.RefNotFound("missing"));

        // Act
        Result<bool> result = await Sut.IsAncestorOfBranchAsync(
            repositoryId, "feat/42-x-run-7", "feat/42-x", TestContext.Current.CancellationToken);

        // Assert
        Error error = result.ErrorOrThrow();
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(RunPublishingErrors.RefNotFoundCode),
            () => error.Kind.ShouldBe(ErrorKind.NotFound));
    }
}
