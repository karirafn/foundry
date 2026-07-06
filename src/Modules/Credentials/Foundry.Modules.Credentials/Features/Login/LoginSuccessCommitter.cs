using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Commits the side-effects of a successful OAuth login: loads <see cref="ClaudeAccount"/>,
/// records the successful login via <see cref="ClaudeAccount.RecordSuccessfulLogin"/>, persists
/// the change, and publishes <see cref="CredentialsValidated"/> so consumers can react.
/// </summary>
internal sealed class LoginSuccessCommitter(
    IServiceScopeFactory scopeFactory,
    ILogger<LoginSuccessCommitter> logger) : ILoginSuccessCommitter
{
    public async Task CommitAsync(AccountIdentity identity, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IIntegrationEventDispatcher eventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            logger.LogWarning("Login success commit skipped: ClaudeAccount not found.");
            return;
        }

        account.RecordSuccessfulLogin(identity.Email, identity.OrgName, identity.SubscriptionType);
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventDispatcher.DispatchAsync(
            [new CredentialsValidated(identity.Email, identity.OrgName, identity.SubscriptionType)],
            cancellationToken);

        logger.LogInformation(
            "OAuth login succeeded: ClaudeAccount updated for {Email}, CredentialsValidated published.",
            identity.Email);
    }
}
