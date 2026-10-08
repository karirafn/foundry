using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Infrastructure;

public sealed class UnreadableTokenMaterializationInterceptor(
    ILogger<UnreadableTokenMaterializationInterceptor> logger)
    : IMaterializationInterceptor
{
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        CheckAndWarn(entity);
        return entity;
    }

    internal void CheckAndWarn(object instance)
    {
        if (instance is Credential { Token: ProviderToken.Unreadable } credential)
        {
            logger.LogWarning(
                "Credential {CredentialId} was loaded with an unreadable accounts.token — " +
                "the stored ciphertext could not be decrypted. The credential will fall back to anonymous access.",
                credential.Id.Value);
        }
    }
}
