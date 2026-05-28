using Foundry.WebApi.Modules.Monitoring.Domain;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure.IssueProviderFactoryTests;

internal sealed class UnknownAccount : Account
{
    public UnknownAccount() : base(AccountId.New())
    {
    }
}
