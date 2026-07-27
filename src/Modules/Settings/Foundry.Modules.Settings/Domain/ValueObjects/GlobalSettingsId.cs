using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

public readonly record struct GlobalSettingsId(Guid Value) : IStronglyTypedId<GlobalSettingsId>
{
    public static readonly GlobalSettingsId Default = new(new Guid("00000000-0000-0000-0000-000000000001"));

    public static GlobalSettingsId New() => new(Guid.NewGuid());

    public static GlobalSettingsId From(Guid value) => new(value);
}
