using Foundry.Modules.Monitoring.Features.Webhooks.StatusBroadcast;
using Foundry.Modules.Settings.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.StatusBroadcast.WebhookPublicBaseUrlChangedBroadcastHandlerTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task BroadcastsThatEveryRepositoryWebhookChanged()
    {
        // Arrange
        RecordingRepositoryWebhookBroadcaster broadcaster = new();
        WebhookPublicBaseUrlChangedBroadcastHandler sut = new(broadcaster);

        // Act
        await sut.HandleAsync(
            new WebhookPublicBaseUrlChanged("https://old.example.com", "https://new.example.com"),
            CancellationToken.None);

        // Assert
        broadcaster.AllChangedCount.ShouldBe(1);
        broadcaster.Changed.ShouldBeEmpty();
    }
}
