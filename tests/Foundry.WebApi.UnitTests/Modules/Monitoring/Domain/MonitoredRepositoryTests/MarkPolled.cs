using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class MarkPolled
{
    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("octocat/hello-world")).Value;

    [Fact]
    public void WhenCalled_SetsLastPolledAt()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, AccountId.New(), null);
        DateTimeOffset polledAt = DateTimeOffset.UtcNow;

        // Act
        repository.MarkPolled(polledAt);

        // Assert
        repository.LastPolledAt.ShouldBe(polledAt);
    }
}
