namespace Foundry.WebApi.Shared.Abstractions;

public abstract class Entity<TId>(TId id) : IEquatable<Entity<TId>>
    where TId : struct, IStronglyTypedId<TId>
{
    public TId Id { get; private set; } = id;

    public bool Equals(Entity<TId>? other) =>
        other is not null && GetType() == other.GetType() && Id.Equals(other.Id);

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
