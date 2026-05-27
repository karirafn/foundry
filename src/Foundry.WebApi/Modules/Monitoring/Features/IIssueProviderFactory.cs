using Foundry.WebApi.Modules.Monitoring.Domain;

namespace Foundry.WebApi.Modules.Monitoring.Features;

public interface IIssueProviderFactory
{
    IIssueProvider CreateProvider(Account account, string token);
}
