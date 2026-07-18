using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class MarkPolled
{
    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    [Fact]
    public void WhenCalled_SetsLastPolledAt()
    {
        // Arrange
        MonitoredRepository repository = MonitoredRepository.Create(ValidSlug, CredentialId.New(), "github.com", null);
        DateTimeOffset polledAt = DateTimeOffset.UtcNow;

        // Act
        repository.MarkPolled(polledAt);

        // Assert
        repository.LastPolledAt.ShouldBe(polledAt);
    }
}
