using System.Reflection;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.AccountTokenChecks;

/// <summary>
/// Fills in the <see cref="Credential.TokenExpiry"/> of accounts saved before token expiry was tracked.
/// Each tick reads the expiry once from the provider for every account whose expiry is
/// <see cref="TokenExpiry.NotChecked"/> and whose token is usable, and stores it. An account the
/// provider gave no reading for stays <see cref="TokenExpiry.NotChecked"/>, so the next tick retries it.
/// </summary>
internal sealed class AccountTokenExpiryBackfillService : PeriodicBackgroundService
{
    private const string DocGenerationEntryAssemblyName = "GetDocument.Insider";

    private static readonly TimeSpan BackfillInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountTokenExpiryBackfillService> _logger;

    public AccountTokenExpiryBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<AccountTokenExpiryBackfillService> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override TimeSpan TickInterval => BackfillInterval;

    protected override bool RunTickOnStartup => true;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // The build-time OpenAPI doc generation tool runs the app with no database to read accounts from.
        Assembly.GetEntryAssembly()?.GetName().Name == DocGenerationEntryAssemblyName
            ? Task.CompletedTask
            : base.ExecuteAsync(stoppingToken);

    protected override Task TickAsync(CancellationToken cancellationToken) => BackfillAsync(cancellationToken);

    internal async Task BackfillAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Credential> accounts = await LoadUncheckedAccountsAsync(cancellationToken);

        foreach (Credential account in accounts)
        {
            try
            {
                await BackfillAccountAsync(account, cancellationToken);
            }
#pragma warning disable CA1031 // One account's failure must not stop the rest of the tick; the next tick retries it.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                _logger.LogError(
                    ex,
                    "Reading the token expiry of account '{CredentialName}' (id '{CredentialId}') failed; the next tick retries it.",
                    account.Name.Value,
                    account.Id);
            }
        }
    }

    private async Task<IReadOnlyList<Credential>> LoadUncheckedAccountsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // TokenExpiry, TokenUsability and TokenValidity are derived on the loaded aggregate (the token is
        // decrypted on load), so the filter runs in memory over the handful of accounts.
        List<Credential> accounts = await dbContext.Set<Credential>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return accounts
            .Where(a => a.TokenExpiry is TokenExpiry.NotChecked
                && a.TokenUsability is TokenUsability.Usable
                && a.TokenValidity is TokenValidity.Valid)
            .ToList();
    }

    private async Task BackfillAccountAsync(Credential account, CancellationToken cancellationToken)
    {
        string token = ((TokenUsability.Usable)account.TokenUsability).Token;

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IAccountTokenValidator validator = scope.ServiceProvider.GetRequiredService<IAccountTokenValidator>();
        AccountTokenVerdicts verdicts = scope.ServiceProvider.GetRequiredService<AccountTokenVerdicts>();

        Result<TokenValidationOutcome> check = await validator.ValidateAsync(account, token, cancellationToken);

        TokenExpiry? expiry = check switch
        {
            Result<TokenValidationOutcome>.Success { Value: TokenValidationOutcome.AuthenticatedOutcome authenticated } =>
                authenticated.TokenExpiry,
            Result<TokenValidationOutcome>.Success { Value: TokenValidationOutcome.GrantReadOutcome grantRead } =>
                grantRead.TokenExpiry,
            Result<TokenValidationOutcome>.Success { Value: TokenValidationOutcome.WrongTokenKindOutcome wrongKind } =>
                wrongKind.TokenExpiry,
            _ => null,
        };

        if (expiry is null)
        {
            // A rejected token is left to the runtime 401 path, the only way an account is marked Invalid;
            // an unreachable provider gives no reading. Either way the account stays NotChecked.
            _logger.LogInformation(
                "The token check of account '{CredentialName}' (id '{CredentialId}') gave no expiry; the next tick retries it.",
                account.Name.Value,
                account.Id);
            return;
        }

        await verdicts.RecordTokenExpiryAsync(account, expiry, cancellationToken);
    }
}
