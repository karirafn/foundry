using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure.IssueProviderFactoryTests;

internal sealed class UnknownAccount : Account
{
    public UnknownAccount() : base(AccountId.New())
    {
    }
}
