using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Features.AccountTokenHealth;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AccountTokenHealth.AccountTokenBroadcastHandlerTests;

public sealed class HandleAsync
{
    private static readonly CredentialId AccountId = CredentialId.From(Guid.Parse("6f0c8a52-77c4-4bfa-9d2e-1b8a3c4d5e6f"));

    private static AccountTokenBroadcastHandler CreateHandler(
        IAccountTokenHealthQueries queries,
        ISystemNotificationBroadcaster broadcaster) =>
        new(queries, broadcaster, NullLogger<AccountTokenBroadcastHandler>.Instance);

    [Fact]
    public async Task WhenTokenIsInvalidated_SendsTheCommittedCountAsAnActiveNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        AccountTokenBroadcastHandler handler = CreateHandler(new FixedCountQueries(2), broadcaster);

        // Act
        await handler.HandleAsync(new CredentialTokenInvalidated(AccountId), TestContext.Current.CancellationToken);

        // Assert
        broadcaster.SentNotifications.ShouldBe(
        [
            new SystemNotification(
                "account-token",
                IsActive: true,
                Message: "2 accounts have invalid tokens — enter new ones to resume monitoring."),
        ]);
    }

    [Fact]
    public async Task WhenTokenIsRevalidatedAndOneRemainsInvalid_SendsTheSingularCopy()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        AccountTokenBroadcastHandler handler = CreateHandler(new FixedCountQueries(1), broadcaster);

        // Act
        await handler.HandleAsync(new CredentialTokenRevalidated(AccountId), TestContext.Current.CancellationToken);

        // Assert
        broadcaster.SentNotifications.ShouldBe(
        [
            new SystemNotification(
                "account-token",
                IsActive: true,
                Message: "1 account has an invalid token — enter a new one to resume monitoring."),
        ]);
    }

    [Fact]
    public async Task WhenTheLastInvalidTokenIsRevalidated_SendsAnInactiveNotification()
    {
        // Arrange
        CapturingSystemNotificationBroadcaster broadcaster = new();
        AccountTokenBroadcastHandler handler = CreateHandler(new FixedCountQueries(0), broadcaster);

        // Act
        await handler.HandleAsync(new CredentialTokenRevalidated(AccountId), TestContext.Current.CancellationToken);

        // Assert
        broadcaster.SentNotifications.ShouldBe([new SystemNotification("account-token", IsActive: false, Message: "")]);
    }

    [Fact]
    public async Task WhenTheBroadcastFails_DoesNotThrow()
    {
        // Arrange — the transition is already committed, so a failed notification must not fail the caller.
        AccountTokenBroadcastHandler handler = CreateHandler(new FixedCountQueries(1), new ThrowingBroadcaster());

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            handler.HandleAsync(new CredentialTokenInvalidated(AccountId), TestContext.Current.CancellationToken));

        // Assert
        thrown.ShouldBeNull();
    }

    private sealed class FixedCountQueries(int count) : IAccountTokenHealthQueries
    {
        public Task<int> CountInvalidAsync(CancellationToken cancellationToken) => Task.FromResult(count);

        public Task<AccountTokenExpiryCounts> CountExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The invalid-token broadcast never reads the expiry counts.");
    }

    private sealed class ThrowingBroadcaster : ISystemNotificationBroadcaster
    {
        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The hub is unavailable.");
    }
}
