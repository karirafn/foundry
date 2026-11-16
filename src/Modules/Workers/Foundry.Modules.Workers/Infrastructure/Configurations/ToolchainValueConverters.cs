using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ImageBuild;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Foundry.Modules.Workers.Infrastructure.Configurations;

/// <summary>Stores a <see cref="Toolchain"/> as the JSON array <c>workers/toolchain.sh</c> prints (ADR 0109).</summary>
internal static class ToolchainValueConverters
{
    internal static readonly ValueConverter<Toolchain, string> Converter = new(
        toolchain => ToolchainJson.Serialize(toolchain),
        json => FromStored(json));

    internal static readonly ValueComparer<Toolchain> Comparer = new(
        (left, right) => AreEqual(left, right),
        toolchain => HashOf(toolchain),
        toolchain => toolchain);

    // For an optional column: EF stores null without calling the converter, so the lambdas never see null.
    internal static readonly ValueConverter<Toolchain?, string> OptionalConverter = new(
        toolchain => ToolchainJson.Serialize(toolchain!),
        json => FromStored(json));

    internal static readonly ValueComparer<Toolchain?> OptionalComparer = new(
        (left, right) => AreEqual(left, right),
        toolchain => toolchain == null ? 0 : HashOf(toolchain),
        toolchain => toolchain);

    // A stored list that no longer reads as a toolchain (edited by hand) counts as unrecorded rather than failing the
    // query.
    private static Toolchain FromStored(string json) => ToolchainJson.TryDeserialize(json) ?? Toolchain.Unrecorded;

    private static bool AreEqual(Toolchain? left, Toolchain? right) =>
        ReferenceEquals(left, right) || (left != null && right != null && left.Entries.SequenceEqual(right.Entries));

    private static int HashOf(Toolchain toolchain) =>
        toolchain.Entries.Aggregate(0, (hash, entry) => HashCode.Combine(hash, entry));
}
