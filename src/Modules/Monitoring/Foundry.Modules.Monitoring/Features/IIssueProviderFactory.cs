using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Features;

public interface IIssueProviderFactory
{
    IIssueProvider CreateProvider(Account account, string token);
}
