using Foundry.Shared;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public abstract class Account : AggregateRoot<AccountId>
{
    protected Account(AccountId id) : base(id)
    {
    }

    public string Name { get; private protected set; } = string.Empty;

    public string SecretKeyName { get; private protected set; } = string.Empty;

    public Uri BaseUrl { get; private protected set; } = null!;
}
