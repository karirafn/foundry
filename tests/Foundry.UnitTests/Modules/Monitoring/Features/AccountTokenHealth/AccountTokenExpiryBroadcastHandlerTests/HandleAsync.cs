using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Features.AccountTokenHealth;
using Foundry.Shared;

using Microsoft.Extensions.Logging.Abstractions;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AccountTokenHealth.AccountTokenExpiryBroadcastHandlerTests;

public sealed class HandleAsync
{
    private static readonly CredentialId AccountId = CredentialId.From(Guid.Parse("3c1d7a40-58e2-4d6b-9f0a-2b7e4c9d1a35"));
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private const string OneExpiringSoon =
        "1 account has a token expiring within 14 days — enter a new one before monitoring stops.";

    private readonly ScriptedExpiryQueries _queries = new() { Counts = new AccountTokenExpiryCounts(ExpiringSoon: 1, Expired: 0) };
    private readonly SwitchableBroadcaster _broadcaster = new();

    public static TheoryData<IDomainEvent> AccountChanges() =>
    [
        new CredentialTokenInvalidated(AccountId),
        new CredentialTokenRevalidated(AccountId),
        new CredentialTokenSet(AccountId),
        new CredentialDeleted(AccountId),
    ];

    [Theory]
    [MemberData(nameof(AccountChanges))]
    public async Task WhenAnAccountChanges_BroadcastsTheCurrentCounts(IDomainEvent change)
    {
        // Arrange
        IDomainEventHandler handler = CreateHandler();

        // Act
        await handler.HandleAsync(change, TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Sent.ShouldBe(
            [new SystemNotification(AccountTokenExpiryNotification.Category, IsActive: true, Message: OneExpiringSoon)]);
    }

    [Fact]
    public async Task WhenCountingIsAsked_UsesTheCurrentTime()
    {
        // Arrange
        IDomainEventHandler handler = CreateHandler();

        // Act
        await handler.HandleAsync(new CredentialTokenSet(AccountId), TestContext.Current.CancellationToken);

        // Assert
        _queries.AskedAt.ShouldBe([Now]);
    }

    [Fact]
    public async Task WhenNoAccountIsExpiringOrExpired_BroadcastsAnInactiveNotification()
    {
        // Arrange
        _queries.Counts = new AccountTokenExpiryCounts(ExpiringSoon: 0, Expired: 0);
        IDomainEventHandler handler = CreateHandler();

        // Act
        await handler.HandleAsync(new CredentialDeleted(AccountId), TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Sent.ShouldBe(
            [new SystemNotification(AccountTokenExpiryNotification.Category, IsActive: false, Message: string.Empty)]);
    }

    [Fact]
    public async Task WhenTheBroadcastFails_DoesNotThrow()
    {
        // Arrange — the account change is already committed, so a failed notification must not fail the caller.
        _broadcaster.Failing = true;
        IDomainEventHandler handler = CreateHandler();

        // Act
        Exception? thrown = await Record.ExceptionAsync(() =>
            handler.HandleAsync(new CredentialTokenSet(AccountId), TestContext.Current.CancellationToken));

        // Assert
        thrown.ShouldBeNull();
    }

    private AccountTokenExpiryBroadcastHandler CreateHandler() =>
        new(new AccountTokenExpiryBroadcaster(
            _queries,
            _broadcaster,
            new FakeTimeProvider(Now),
            NullLogger<AccountTokenExpiryBroadcaster>.Instance));
}
