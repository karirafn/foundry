using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Providers;

namespace Foundry.UnitTests.Fakes.Monitoring;

internal sealed class SingleIssueProviderFactory(IIssueProvider provider) : IIssueProviderFactory
{
    public IIssueProvider CreateProvider(Credential credential, string token) => provider;
}
