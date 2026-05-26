namespace Foundry.WebApi.Shared.Abstractions;

public interface IStronglyTypedId<TSelf> : IEquatable<TSelf>
    where TSelf : struct, IStronglyTypedId<TSelf>
{
    Guid Value { get; }

    static abstract TSelf From(Guid value);
}
