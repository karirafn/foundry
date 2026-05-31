using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

internal static class ReviewCommentsJsonConversion
{
    public static ValueConverter<IReadOnlyList<ReviewComment>, string> Converter { get; } = new(
        comments => JsonSerializer.Serialize(comments, (JsonSerializerOptions?)null),
        json => (IReadOnlyList<ReviewComment>)(JsonSerializer.Deserialize<List<ReviewComment>>(
            json,
            (JsonSerializerOptions?)null)
            ?? new List<ReviewComment>()));

    public static ValueComparer<IReadOnlyList<ReviewComment>> Comparer { get; } = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        comments => comments.Aggregate(
            0,
            (hash, c) => HashCode.Combine(hash, c.GetHashCode())),
        comments => (IReadOnlyList<ReviewComment>)comments.ToList());
}
