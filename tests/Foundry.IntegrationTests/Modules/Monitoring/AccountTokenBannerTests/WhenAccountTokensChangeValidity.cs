using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.AccountTokenChecks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.AccountTokenBannerTests;

/// <summary>
/// A token verdict that moves an account between Valid and Invalid broadcasts the account-token banner
/// with the count of Invalid accounts read from committed state.
/// </summary>
public sealed class WhenAccountTokensChangeValidity : IAsyncDisposable
{
    private const string Plural = "2 accounts have invalid tokens — enter new ones to resume monitoring.";
    private const string Singular = "1 account has an invalid token — enter a new one to resume monitoring.";

    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecheckAt = DetectedAt + Credential.TokenRecheckCooldown;

    private readonly CapturingSystemNotificationBroadcaster _broadcaster = new();
    private readonly FoundryWebAppFactory _factory;

    public WhenAccountTokensChangeValidity()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<ISystemNotificationBroadcaster>();
            services.AddSingleton<ISystemNotificationBroadcaster>(_broadcaster);
        });
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenTwoBecomeInvalidThenBothRecover_BannerCountsDownAndClears()
    {
        // Arrange
        CredentialId first = CredentialId.From(await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "first"));
        CredentialId second = CredentialId.From(await AccountSeeder.SeedGitLabAccountAsync(_factory, name: "second"));

        // Act
        await ConfirmRejectionAsync(first);
        await ConfirmRejectionAsync(second);
        await RecheckAuthenticatesAsync(first);
        await RecheckAuthenticatesAsync(second);

        // Assert
        AccountTokenBanners().ShouldBe(
        [
            new SystemNotification(AccountTokenNotification.Category, IsActive: true, Message: Singular),
            new SystemNotification(AccountTokenNotification.Category, IsActive: true, Message: Plural),
            new SystemNotification(AccountTokenNotification.Category, IsActive: true, Message: Singular),
            new SystemNotification(AccountTokenNotification.Category, IsActive: false, Message: string.Empty),
        ]);
    }

    [Fact]
    public async Task WhenAnInvalidAccountIsRecheckedAndStillRejected_NothingIsBroadcast()
    {
        // Arrange
        CredentialId account = CredentialId.From(await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "first"));
        await ConfirmRejectionAsync(account);
        int broadcastsBefore = AccountTokenBanners().Count;

        // Act
        await RunVerdictAsync(
            account,
            TokenValidationOutcome.AuthenticationFailed(),
            (verdicts, resolved, token) => verdicts.RecheckAsync(resolved, token, RecheckAt, TestContext.Current.CancellationToken));

        // Assert
        AccountTokenBanners().Count.ShouldBe(broadcastsBefore);
    }

    private List<SystemNotification> AccountTokenBanners() =>
        [.. _broadcaster.SentNotifications.Where(n => n.Category == AccountTokenNotification.Category)];

    private Task ConfirmRejectionAsync(CredentialId account) =>
        RunVerdictAsync(
            account,
            TokenValidationOutcome.AuthenticationFailed(),
            (verdicts, resolved, token) => verdicts.ConfirmRejectionAsync(resolved, token, DetectedAt, TestContext.Current.CancellationToken));

    private Task RecheckAuthenticatesAsync(CredentialId account) =>
        RunVerdictAsync(
            account,
            TokenValidationOutcome.Authenticated("octocat"),
            (verdicts, resolved, token) => verdicts.RecheckAsync(resolved, token, RecheckAt, TestContext.Current.CancellationToken));

    private async Task RunVerdictAsync(
        CredentialId account,
        TokenValidationOutcome outcome,
        Func<AccountTokenVerdicts, Credential, string, Task<bool>> verdict)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        Credential resolved = await db.Set<Credential>()
            .AsNoTracking()
            .SingleAsync(c => c.Id == account, TestContext.Current.CancellationToken);
        string token = resolved.TokenUsability switch
        {
            TokenUsability.Usable usable => usable.Token,
            TokenUsability.Invalid invalid => invalid.Token,
            _ => throw new InvalidOperationException("The seeded account has no readable token."),
        };
        AccountTokenVerdicts verdicts = new(
            db,
            new FixedTokenValidator(outcome),
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>(),
            NullLogger<AccountTokenVerdicts>.Instance);

        await verdict(verdicts, resolved, token);
    }

    private sealed class FixedTokenValidator(TokenValidationOutcome outcome) : IAccountTokenValidator
    {
        public Task<Result<TokenValidationOutcome>> ValidateAsync(
            Credential credential,
            string token,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<TokenValidationOutcome>.Ok(outcome));
    }
}
