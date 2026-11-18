using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Features.AccountTokenHealth;
using Foundry.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AccountTokenHealth.AccountTokenExpiryAlertServiceTests;

/// <summary>
/// The hourly tick of <see cref="AccountTokenExpiryAlertService"/> broadcasts only when the counts differ
/// from the last ones it sent.
/// </summary>
public sealed class RecountAsync
{
    private readonly ScriptedExpiryQueries _queries = new();
    private readonly SwitchableBroadcaster _broadcaster = new();

    [Fact]
    public async Task OnTheFirstTick_BroadcastsEvenWhenNothingIsExpiring()
    {
        // Arrange
        AccountTokenExpiryAlertService sut = BuildService();

        // Act
        await sut.RecountAsync(TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Sent.ShouldBe(
            [new SystemNotification(AccountTokenExpiryNotification.Category, IsActive: false, Message: string.Empty)]);
    }

    [Fact]
    public async Task WhenTheCountsAreUnchanged_DoesNotBroadcastAgain()
    {
        // Arrange
        _queries.Counts = new AccountTokenExpiryCounts(ExpiringSoon: 1, Expired: 0);
        AccountTokenExpiryAlertService sut = BuildService();
        await sut.RecountAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.RecountAsync(TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenAnAccountCrossesIntoTheWindow_BroadcastsTheNewCounts()
    {
        // Arrange
        AccountTokenExpiryAlertService sut = BuildService();
        await sut.RecountAsync(TestContext.Current.CancellationToken);
        _queries.Counts = new AccountTokenExpiryCounts(ExpiringSoon: 1, Expired: 0);

        // Act
        await sut.RecountAsync(TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Sent[^1].ShouldBe(new SystemNotification(
            AccountTokenExpiryNotification.Category,
            IsActive: true,
            Message: "1 account has a token expiring within 14 days — enter a new one before monitoring stops."));
    }

    [Fact]
    public async Task WhenOnlyTheSplitBetweenSoonAndExpiredChanges_BroadcastsTheNewCounts()
    {
        // Arrange — the same account moves from expiring soon to expired; the total is unchanged.
        _queries.Counts = new AccountTokenExpiryCounts(ExpiringSoon: 1, Expired: 0);
        AccountTokenExpiryAlertService sut = BuildService();
        await sut.RecountAsync(TestContext.Current.CancellationToken);
        _queries.Counts = new AccountTokenExpiryCounts(ExpiringSoon: 0, Expired: 1);

        // Act
        await sut.RecountAsync(TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Sent[^1].Message.ShouldBe("1 account has an expired token — enter a new one before monitoring stops.");
    }

    [Fact]
    public async Task WhenTheLastBroadcastFailed_TheNextTickRetriesTheSameCounts()
    {
        // Arrange
        _queries.Counts = new AccountTokenExpiryCounts(ExpiringSoon: 2, Expired: 0);
        _broadcaster.Failing = true;
        AccountTokenExpiryAlertService sut = BuildService();
        await sut.RecountAsync(TestContext.Current.CancellationToken);
        _broadcaster.Failing = false;

        // Act
        await sut.RecountAsync(TestContext.Current.CancellationToken);

        // Assert
        _broadcaster.Sent.Count.ShouldBe(1);
    }

    private AccountTokenExpiryAlertService BuildService()
    {
        ServiceCollection services = new();
        services.AddSingleton<IAccountTokenHealthQueries>(_queries);
        services.AddSingleton<ISystemNotificationBroadcaster>(_broadcaster);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)));
        services.AddLogging();
        services.AddScoped<AccountTokenExpiryBroadcaster>();

        ServiceProvider provider = services.BuildServiceProvider();
        return new AccountTokenExpiryAlertService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AccountTokenExpiryAlertService>.Instance);
    }
}
