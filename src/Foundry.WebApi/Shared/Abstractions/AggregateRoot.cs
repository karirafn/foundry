namespace Foundry.WebApi.Shared.Abstractions;

public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id)
    where TId : struct, IStronglyTypedId<TId>;
