using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Entities.MonitoredRepositoryTests;

public sealed class WriteProbeVerdict
{
    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    private static MonitoredRepository CreateRepository() =>
        MonitoredRepository.Create(ValidSlug, "github.com", null);

    [Fact]
    public void WhenCreated_WriteProbeVerdictIsUnknown()
    {
        // Arrange / Act
        MonitoredRepository repository = CreateRepository();

        // Assert
        repository.WriteProbeVerdict.ShouldBeOfType<Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Unknown>();
    }

    [Fact]
    public void WhenSetToGranted_WriteProbeVerdictIsGranted()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        repository.SetWriteProbeVerdict(new Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Granted());

        // Assert
        repository.WriteProbeVerdict.ShouldBeOfType<Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Granted>();
    }

    [Fact]
    public void WhenSetToDenied_WriteProbeVerdictIsDenied()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();

        // Act
        repository.SetWriteProbeVerdict(new Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Denied());

        // Assert
        repository.WriteProbeVerdict.ShouldBeOfType<Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Denied>();
    }

    [Fact]
    public void WhenSetToUnknown_WriteProbeVerdictIsUnknown()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Granted());

        // Act
        repository.SetWriteProbeVerdict(new Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Unknown());

        // Assert
        repository.WriteProbeVerdict.ShouldBeOfType<Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Unknown>();
    }

    [Fact]
    public void WhenVerdictChangedMultipleTimes_ReflectsLastValue()
    {
        // Arrange
        MonitoredRepository repository = CreateRepository();
        repository.SetWriteProbeVerdict(new Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Granted());

        // Act
        repository.SetWriteProbeVerdict(new Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Denied());

        // Assert
        repository.WriteProbeVerdict.ShouldBeOfType<Foundry.Modules.Monitoring.Domain.ValueObjects.WriteProbeVerdict.Denied>();
    }
}
