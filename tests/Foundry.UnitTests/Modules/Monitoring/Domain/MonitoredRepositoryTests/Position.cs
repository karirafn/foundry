using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class Position
{
    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    [Fact]
    public void WhenCreatedWithExplicitPosition_HasThatPosition()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", null, position: 3);

        // Assert
        repository.Position.ShouldBe(3);
    }

    [Fact]
    public void WhenCreatedWithZeroPosition_HasZeroPosition()
    {
        // Arrange
        RepositorySlug slug = ValidSlug;

        // Act
        MonitoredRepository repository = MonitoredRepository.Create(slug, "github.com", null, position: 0);

        // Assert
        repository.Position.ShouldBe(0);
    }

    [Fact]
    public void WhenSetPositionCalled_UpdatesPosition()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, "github.com", null, position: 0);

        // Act
        repository.SetPosition(5);

        // Assert
        repository.Position.ShouldBe(5);
    }
}
