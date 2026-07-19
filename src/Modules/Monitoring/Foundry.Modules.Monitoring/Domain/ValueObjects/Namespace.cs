using System.Text.RegularExpressions;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

public sealed partial record Namespace
{
    public string Value { get; }

    public int SegmentCount { get; }

    private Namespace(string value, int segmentCount)
    {
        Value = value;
        SegmentCount = segmentCount;
    }

    public static Result<Namespace> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<Namespace>.Fail(NamespaceErrors.InvalidFormat);
        }

        string[] segments = value.Split('/');

        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || !AllowedSegmentCharacters().IsMatch(segment))
            {
                return Result<Namespace>.Fail(NamespaceErrors.InvalidFormat);
            }

            if (segment is "." or "..")
            {
                return Result<Namespace>.Fail(NamespaceErrors.InvalidFormat);
            }
        }

        return new Namespace(value, segments.Length);
    }

    public bool IsPrefixOf(RepositorySlug slug)
    {
        string owner = slug.Owner;

        if (Value == owner)
        {
            return true;
        }

        return owner.StartsWith(Value + "/", StringComparison.Ordinal);
    }

    public static IReadOnlyList<Namespace> PrefixesOf(RepositorySlug slug)
    {
        string owner = slug.Owner;
        string[] segments = owner.Split('/');

        List<Namespace> prefixes = [];

        for (int length = segments.Length; length >= 1; length--)
        {
            string prefix = string.Join('/', segments[..length]);
            prefixes.Add(new Namespace(prefix, length));
        }

        return prefixes;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
    private static partial Regex AllowedSegmentCharacters();
}
