using Foundry.Modules.Issues.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// Stores an <see cref="ImplementRunOrigin"/> in the shared <c>implement_run_origin</c> column as a stable token, so
/// renaming an enum member never changes stored rows.
/// </summary>
internal static class ImplementRunOriginConversion
{
    internal const string ColumnName = "implement_run_origin";

    private const int MaxLength = 20;

    private const string FreshToken = "fresh";

    private const string ContinuationToken = "continuation";

    private static readonly ValueConverter<ImplementRunOrigin, string> Converter = new(
        origin => ToToken(origin),
        token => FromToken(token));

    internal static void Configure<TProperty>(PropertyBuilder<TProperty> property) =>
        property
            .HasConversion(Converter)
            .HasMaxLength(MaxLength)
            .IsUnicode(false)
            .HasColumnName(ColumnName);

    private static string ToToken(ImplementRunOrigin origin) => origin switch
    {
        ImplementRunOrigin.Fresh => FreshToken,
        ImplementRunOrigin.Continuation => ContinuationToken,
        _ => throw new InvalidOperationException($"Unknown implement run origin: {origin}"),
    };

    private static ImplementRunOrigin FromToken(string token) => token switch
    {
        FreshToken => ImplementRunOrigin.Fresh,
        ContinuationToken => ImplementRunOrigin.Continuation,
        _ => throw new InvalidOperationException($"Unknown stored implement run origin: {token}"),
    };
}
