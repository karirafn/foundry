using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Real implementation of <see cref="ILoginSuccessCommitter"/> that remains in the Workers
/// module until step 6 inverts the cross-module write to an integration event.
/// Opens a scoped service scope to access the DbContext and integration event dispatcher,
/// then persists the account identity, clears <c>AuthInvalidPause</c> via
/// <see cref="GlobalSettings.ResumeDispatch"/>, and publishes <see cref="DispatchResumed"/>
/// so the Phase-1 handler re-queues auth-invalid failed issues.
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

        GlobalSettings? settings = await dbContext.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            logger.LogWarning("Login success commit skipped: GlobalSettings not found.");
            return;
        }

        settings.SetOAuthAccountIdentity(identity.Email, identity.OrgName, identity.SubscriptionType);
        settings.ResumeDispatch();
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventDispatcher.DispatchAsync([new DispatchResumed()], cancellationToken);

        logger.LogInformation(
            "OAuth login succeeded: account identity committed for {Email}, dispatch resumed.",
            identity.Email);
    }
}
