using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Features.Providers;

public interface IIssueProviderFactory
{
    IIssueProvider CreateProvider(Credential credential, string token);
}
