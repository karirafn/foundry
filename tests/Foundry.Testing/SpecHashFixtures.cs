using Foundry.Shared;

namespace Foundry.Testing;

public static class SpecHashFixtures
{
    /// <summary>A well-formed spec hash for tests that do not care which body it fingerprints.</summary>
    public static SpecHash Any { get; } = SpecHash.From(new string('a', 64));

    /// <summary>A well-formed spec hash distinct from <see cref="Any"/>.</summary>
    public static SpecHash Other { get; } = SpecHash.From(new string('b', 64));
}
