using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared.Infrastructure.Outbox;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.ClarificationInputTests;

public sealed class RoundTrip
{
    [Fact]
    public void AwaitingInputArrived_RoundTripsItsInputThroughTheOutboxSerializer()
    {
        // Arrange
        AwaitingInputArrived arrived = new(
            MonitoredRepositoryId.New(),
            3,
            ClarificationInput.FromComments("Use Postgres."),
            new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

        // Act
        string json = JsonSerializer.Serialize(arrived, OutboxSerializerOptions.Default);
        AwaitingInputArrived? read = JsonSerializer.Deserialize<AwaitingInputArrived>(json, OutboxSerializerOptions.Default);

        // Assert
        read.ShouldBe(arrived);
    }

    [Fact]
    public void AwaitingInputArrived_RoundTripsADescriptionEditNextToComments()
    {
        // Arrange
        AwaitingInputArrived arrived = new(
            MonitoredRepositoryId.New(),
            3,
            new ClarificationInput([new InputPart.Comments("See the description."), new InputPart.DescriptionEdited()]),
            new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

        // Act
        string json = JsonSerializer.Serialize(arrived, OutboxSerializerOptions.Default);
        AwaitingInputArrived? read = JsonSerializer.Deserialize<AwaitingInputArrived>(json, OutboxSerializerOptions.Default);

        // Assert
        json.ShouldContain("""{"kind":"descriptionEdited"}""");
        read.ShouldBe(arrived);
    }
}
