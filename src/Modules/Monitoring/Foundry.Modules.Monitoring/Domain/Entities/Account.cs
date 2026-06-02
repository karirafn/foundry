using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

public abstract class Account : AggregateRoot<AccountId>
{
    protected Account(AccountId id) : base(id)
    {
    }

    public string Name { get; private protected set; } = string.Empty;

    public string SecretKeyName { get; private protected set; } = string.Empty;

    public Uri BaseUrl { get; private protected set; } = null!;

    public abstract Uri ApiBaseUrl { get; }
}
