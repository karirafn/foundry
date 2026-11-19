using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class Remove
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(RepositorySlug.Create("octocat/hello-world").ValueOrThrow(), "github.com", null)
            .ValueOrThrow();

    [Fact]
    public void SetsRemovedAt()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        Result result = repository.Remove(Now);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        repository.RemovedAt.ShouldBe(Now);
    }

    [Fact]
    public void RaisesTheRemovedDomainEvent()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        repository.Remove(Now);

        // Assert
        repository.DomainEvents.ShouldHaveSingleItem()
            .ShouldBe(new MonitoredRepositoryRemovedDomainEvent(repository.Id, Now));
    }

    [Fact]
    public void WhenAlreadyRemoved_FailsWithoutASecondEvent()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.Remove(Now);

        // Act
        Result result = repository.Remove(Now.AddMinutes(5));

        // Assert
        result.ShouldBeOfType<Result.Failure>().Error.Code.ShouldBe(MonitoredRepositoryErrors.AlreadyRemovedCode);
        repository.DomainEvents.Count.ShouldBe(1);
        repository.RemovedAt.ShouldBe(Now);
    }
}
