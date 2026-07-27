using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Features.Providers;

internal interface IIssueProviderFactory
{
    IIssueProvider CreateProvider(Credential credential, string token);
}
