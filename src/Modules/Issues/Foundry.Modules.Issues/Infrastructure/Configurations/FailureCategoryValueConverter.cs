using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Issues.Infrastructure.Configurations;

/// <summary>
/// EF value converter for <see cref="FailureCategory"/>.
/// Maps the value object to and from the failure_category TEXT column.
/// The <c>to</c> direction stores <see cref="FailureCategory.Value"/> unchanged.
/// The <c>from</c> direction uses <see cref="LenientFromToken"/> — unknown stored tokens
/// are coalesced defensively to <see cref="FailureCategory.NonZeroExit"/> without throwing,
/// so that a stale or legacy row never makes every query throw and disables auto-retry.
/// In the current closed system all writers are typed, so an unknown token cannot occur
/// through normal operation; the coalesce is a belt-and-braces safeguard.
/// </summary>
internal static class FailureCategoryValueConverter
{
    public static ValueConverter<FailureCategory, string> Converter { get; } = new(
        category => category.Value,
        token => LenientFromToken(token));

    private static FailureCategory LenientFromToken(string token) =>
        FailureCategory.Create(token) is Result<FailureCategory>.Success ok
            ? ok.Value
            : FailureCategory.NonZeroExit;
}
