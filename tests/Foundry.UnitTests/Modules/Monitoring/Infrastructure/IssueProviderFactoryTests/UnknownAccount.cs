using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.IssueProviderFactoryTests;

internal sealed class UnknownAccount : Credential
{
    public UnknownAccount() : base(CredentialId.New())
    {
    }

    public override Uri ApiBaseUrl => throw new NotSupportedException();
}
