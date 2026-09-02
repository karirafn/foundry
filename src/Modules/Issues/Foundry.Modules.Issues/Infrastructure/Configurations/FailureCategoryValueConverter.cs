using Foundry.Modules.Workers.Contracts;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// EF value converter for <see cref="FailureCategory"/>.
/// Maps the value object to and from the failure_category TEXT column.
/// The <c>from</c> direction uses <see cref="FailureCategory.FromToken"/> (strict) —
/// only the ten known tokens are valid; an unknown stored token throws, surfacing
/// any future rogue writer immediately.
/// </summary>
internal static class FailureCategoryValueConverter
{
    public static ValueConverter<FailureCategory, string> Converter { get; } = new(
        category => category.Value,
        token => FailureCategory.FromToken(token));
}
