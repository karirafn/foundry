using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class SetPosition
{
    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(ValidSlug, AccountId.New(), "github.com", null);

    [Fact]
    public void WhenPositionIsNegative_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        ArgumentOutOfRangeException ex = Should.Throw<ArgumentOutOfRangeException>(
            () => repository.SetPosition(-1));

        // Assert
        ex.ParamName.ShouldBe("position");
    }

    [Fact]
    public void WhenPositionIsZero_SetsPosition()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        repository.SetPosition(0);

        // Assert
        repository.Position.ShouldBe(0);
    }

    [Fact]
    public void WhenPositionIsPositive_SetsPosition()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        repository.SetPosition(42);

        // Assert
        repository.Position.ShouldBe(42);
    }
}
