using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.AccountTokenChecks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.CredentialTokenVersionTests;

/// <summary>
/// <see cref="Credential.TokenVersion"/> is a concurrency token: a token verdict about a token that was
/// replaced while it was being checked is dropped, and a verdict never blocks a rotation.
/// </summary>
public sealed class WhenTokenVerdictRacesRotation : IAsyncDisposable
{
    private static readonly DateTimeOffset DetectedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecheckAt = DetectedAt + Credential.TokenRecheckCooldown;

    private readonly FoundryWebAppFactory _factory;

    public WhenTokenVerdictRacesRotation()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenRotationCommitsBeforeRecheckSaves_RecheckVerdictIsDropped()
    {
        // Arrange — the recheck resolves the account (untracked, as the resolver does) at the old version.
        CredentialId accountId = await SeedInvalidAccountAsync();
        using IServiceScope recheckScope = _factory.Services.CreateScope();
        DbContext recheckDb = recheckScope.ServiceProvider.GetRequiredService<DbContext>();
        Credential resolved = await recheckDb.Set<Credential>()
            .AsNoTracking()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
        string rotatedToken = string.Concat("ghp_", "rotated");
        await RotateAsync(accountId, rotatedToken);
        AccountTokenVerdicts verdicts = new(
            recheckDb,
            new FixedTokenValidator(TokenValidationOutcome.AuthenticationFailed()),
            new NullDomainEventDispatcher(),
            NullLogger<AccountTokenVerdicts>.Instance);

        // Act
        bool valid = await verdicts.RecheckAsync(
            resolved,
            ((TokenUsability.Invalid)resolved.TokenUsability).Token,
            RecheckAt,
            TestContext.Current.CancellationToken);

        // Assert
        valid.ShouldBeFalse();
        Credential stored = await LoadAsync(accountId);
        stored.ShouldSatisfyAllConditions(
            () => stored.TokenValidity.ShouldBeOfType<TokenValidity.Valid>(),
            () => stored.TokenUsability.ShouldBe(new TokenUsability.Usable(rotatedToken)),
            () => stored.TokenVersion.ShouldBe(resolved.TokenVersion + 1));
    }

    [Fact]
    public async Task WhenRotationCommitsBeforeConfirmSaves_ConfirmVerdictIsDropped()
    {
        // Arrange
        CredentialId accountId = await SeedValidAccountAsync();
        using IServiceScope confirmScope = _factory.Services.CreateScope();
        DbContext confirmDb = confirmScope.ServiceProvider.GetRequiredService<DbContext>();
        Credential resolved = await confirmDb.Set<Credential>()
            .AsNoTracking()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
        await RotateAsync(accountId, string.Concat("ghp_", "rotated"));
        AccountTokenVerdicts verdicts = new(
            confirmDb,
            new FixedTokenValidator(TokenValidationOutcome.AuthenticationFailed()),
            new NullDomainEventDispatcher(),
            NullLogger<AccountTokenVerdicts>.Instance);

        // Act
        bool marked = await verdicts.ConfirmRejectionAsync(
            resolved,
            ((TokenUsability.Usable)resolved.TokenUsability).Token,
            DetectedAt,
            TestContext.Current.CancellationToken);

        // Assert
        marked.ShouldBeFalse();
        (await LoadAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }

    [Fact]
    public async Task WhenRotationCommitsBeforeConfirmSaves_DroppedVerdictDispatchesNothing()
    {
        // Arrange
        CredentialId accountId = await SeedValidAccountAsync();
        using IServiceScope confirmScope = _factory.Services.CreateScope();
        DbContext confirmDb = confirmScope.ServiceProvider.GetRequiredService<DbContext>();
        Credential resolved = await confirmDb.Set<Credential>()
            .AsNoTracking()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
        await RotateAsync(accountId, string.Concat("ghp_", "rotated"));
        CapturingDomainEventDispatcher dispatcher = new();
        AccountTokenVerdicts verdicts = new(
            confirmDb,
            new FixedTokenValidator(TokenValidationOutcome.AuthenticationFailed()),
            dispatcher,
            NullLogger<AccountTokenVerdicts>.Instance);

        // Act
        await verdicts.ConfirmRejectionAsync(
            resolved,
            ((TokenUsability.Usable)resolved.TokenUsability).Token,
            DetectedAt,
            TestContext.Current.CancellationToken);

        // Assert
        dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenConfirmSaves_DispatchesCredentialTokenInvalidated()
    {
        // Arrange
        CredentialId accountId = await SeedValidAccountAsync();
        using IServiceScope confirmScope = _factory.Services.CreateScope();
        DbContext confirmDb = confirmScope.ServiceProvider.GetRequiredService<DbContext>();
        Credential resolved = await confirmDb.Set<Credential>()
            .AsNoTracking()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
        CapturingDomainEventDispatcher dispatcher = new();
        AccountTokenVerdicts verdicts = new(
            confirmDb,
            new FixedTokenValidator(TokenValidationOutcome.AuthenticationFailed()),
            dispatcher,
            NullLogger<AccountTokenVerdicts>.Instance);

        // Act
        await verdicts.ConfirmRejectionAsync(
            resolved,
            ((TokenUsability.Usable)resolved.TokenUsability).Token,
            DetectedAt,
            TestContext.Current.CancellationToken);

        // Assert
        dispatcher.DispatchedEvents.ShouldBe([new CredentialTokenInvalidated(accountId)]);
    }

    [Fact]
    public async Task WhenRecheckAuthenticates_DispatchesCredentialTokenRevalidated()
    {
        // Arrange
        CredentialId accountId = await SeedInvalidAccountAsync();
        using IServiceScope recheckScope = _factory.Services.CreateScope();
        DbContext recheckDb = recheckScope.ServiceProvider.GetRequiredService<DbContext>();
        Credential resolved = await recheckDb.Set<Credential>()
            .AsNoTracking()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
        CapturingDomainEventDispatcher dispatcher = new();
        AccountTokenVerdicts verdicts = new(
            recheckDb,
            new FixedTokenValidator(TokenValidationOutcome.Authenticated("octocat", new TokenExpiry.NotChecked(), [])),
            dispatcher,
            NullLogger<AccountTokenVerdicts>.Instance);

        // Act
        await verdicts.RecheckAsync(
            resolved,
            ((TokenUsability.Invalid)resolved.TokenUsability).Token,
            RecheckAt,
            TestContext.Current.CancellationToken);

        // Assert
        dispatcher.DispatchedEvents.ShouldBe([new CredentialTokenRevalidated(accountId)]);
    }

    [Fact]
    public async Task WhenRecheckStampsAnInvalidAccount_DispatchesNothing()
    {
        // Arrange
        CredentialId accountId = await SeedInvalidAccountAsync();
        using IServiceScope recheckScope = _factory.Services.CreateScope();
        DbContext recheckDb = recheckScope.ServiceProvider.GetRequiredService<DbContext>();
        Credential resolved = await recheckDb.Set<Credential>()
            .AsNoTracking()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
        CapturingDomainEventDispatcher dispatcher = new();
        AccountTokenVerdicts verdicts = new(
            recheckDb,
            new FixedTokenValidator(TokenValidationOutcome.AuthenticationFailed()),
            dispatcher,
            NullLogger<AccountTokenVerdicts>.Instance);

        // Act
        await verdicts.RecheckAsync(
            resolved,
            ((TokenUsability.Invalid)resolved.TokenUsability).Token,
            RecheckAt,
            TestContext.Current.CancellationToken);

        // Assert
        dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRecheckStampsBeforeRotationSaves_RotationStillSaves()
    {
        // Arrange — the rotation loads the account tracked, then a recheck stamps LastCheckedAt first.
        CredentialId accountId = await SeedInvalidAccountAsync();
        using IServiceScope rotationScope = _factory.Services.CreateScope();
        DbContext rotationDb = rotationScope.ServiceProvider.GetRequiredService<DbContext>();
        GitHubCredential rotating = (GitHubCredential)await rotationDb.Set<Credential>()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
        int loadedVersion = rotating.TokenVersion;

        using (IServiceScope recheckScope = _factory.Services.CreateScope())
        {
            DbContext recheckDb = recheckScope.ServiceProvider.GetRequiredService<DbContext>();
            Credential resolved = await recheckDb.Set<Credential>()
                .AsNoTracking()
                .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
            AccountTokenVerdicts verdicts = new(
                recheckDb,
                new FixedTokenValidator(TokenValidationOutcome.AuthenticationFailed()),
                new NullDomainEventDispatcher(),
                NullLogger<AccountTokenVerdicts>.Instance);
            await verdicts.RecheckAsync(
                resolved,
                ((TokenUsability.Invalid)resolved.TokenUsability).Token,
                RecheckAt,
                TestContext.Current.CancellationToken);
        }

        (await LoadAsync(accountId)).TokenValidity.ShouldBe(new TokenValidity.Invalid(DetectedAt, RecheckAt));
        string rotatedToken = string.Concat("ghp_", "rotated");
        rotating.Update(rotating.Name, new IssuedToken(rotatedToken, new TokenExpiry.NotChecked()), rotating.BaseUrl);

        // Act
        await rotationDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Credential stored = await LoadAsync(accountId);
        stored.ShouldSatisfyAllConditions(
            () => stored.TokenValidity.ShouldBeOfType<TokenValidity.Valid>(),
            () => stored.TokenUsability.ShouldBe(new TokenUsability.Usable(rotatedToken)),
            () => stored.TokenVersion.ShouldBe(loadedVersion + 1));
    }

    [Fact]
    public async Task WhenConfirmMarksInvalidBeforeRotationSaves_RotationLeavesNewTokenValid()
    {
        // Arrange — the rotation loads the Valid account, then a confirm about the old token marks it Invalid.
        CredentialId accountId = await SeedValidAccountAsync();
        using IServiceScope rotationScope = _factory.Services.CreateScope();
        DbContext rotationDb = rotationScope.ServiceProvider.GetRequiredService<DbContext>();
        GitHubCredential rotating = (GitHubCredential)await rotationDb.Set<Credential>()
            .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);

        using (IServiceScope confirmScope = _factory.Services.CreateScope())
        {
            DbContext confirmDb = confirmScope.ServiceProvider.GetRequiredService<DbContext>();
            Credential resolved = await confirmDb.Set<Credential>()
                .AsNoTracking()
                .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
            AccountTokenVerdicts verdicts = new(
                confirmDb,
                new FixedTokenValidator(TokenValidationOutcome.AuthenticationFailed()),
                new NullDomainEventDispatcher(),
                NullLogger<AccountTokenVerdicts>.Instance);
            await verdicts.ConfirmRejectionAsync(
                resolved,
                ((TokenUsability.Usable)resolved.TokenUsability).Token,
                DetectedAt,
                TestContext.Current.CancellationToken);
        }

        (await LoadAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Invalid>();
        rotating.Update(rotating.Name, new IssuedToken(string.Concat("ghp_", "rotated"), new TokenExpiry.NotChecked()), rotating.BaseUrl);
        CredentialConfiguration.WriteTokenValidity(rotationDb.Entry<Credential>(rotating));

        // Act
        await rotationDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        (await LoadAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }
    private async Task<CredentialId> SeedValidAccountAsync()
    {
        Guid id = await AccountSeeder.SeedGitHubAccountAsync(_factory, token: string.Concat("ghp_", "original"));
        return CredentialId.From(id);
    }

    private async Task<CredentialId> SeedInvalidAccountAsync()
    {
        CredentialId id = await SeedValidAccountAsync();
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        Credential account = await db.Set<Credential>().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
        account.MarkTokenInvalid(DetectedAt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task RotateAsync(CredentialId id, string token)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        GitHubCredential account = (GitHubCredential)await db.Set<Credential>()
            .SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
        account.Update(account.Name, new IssuedToken(token, new TokenExpiry.NotChecked()), account.BaseUrl);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Credential> LoadAsync(CredentialId id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await db.Set<Credential>().AsNoTracking().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
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
