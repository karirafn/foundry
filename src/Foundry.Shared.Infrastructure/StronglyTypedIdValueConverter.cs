using System.Linq.Expressions;
using System.Reflection;

using Foundry.Shared;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Shared.Infrastructure;

public sealed class StronglyTypedIdValueConverter<TId>()
    : ValueConverter<TId, Guid>(id => id.Value, BuildFromProvider())
    where TId : struct, IStronglyTypedId<TId>
{
    private static Expression<Func<Guid, TId>> BuildFromProvider()
    {
        ParameterExpression parameter = Expression.Parameter(typeof(Guid), "guid");
        MethodInfo fromMethod = typeof(TId).GetMethod(
            nameof(IStronglyTypedId<TId>.From),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Guid)])
            ?? throw new InvalidOperationException(
                $"{typeof(TId).Name} does not have a public static From(Guid) method.");
        MethodCallExpression call = Expression.Call(null, fromMethod, parameter);
        return Expression.Lambda<Func<Guid, TId>>(call, parameter);
    }
}
