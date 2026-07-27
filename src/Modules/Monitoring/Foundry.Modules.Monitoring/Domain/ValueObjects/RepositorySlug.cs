using System.Text.RegularExpressions;

using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

public sealed partial record RepositorySlug
{
    public string Owner { get; }

    public string Name { get; }

    public string FullPath => $"{Owner}/{Name}";

    private RepositorySlug(string owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    public static Result<RepositorySlug> Create(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result<RepositorySlug>.Fail(RepositorySlugErrors.InvalidFormat);
        }

        string[] parts = slug.Split('/');

        if (parts.Length < 2)
        {
            return Result<RepositorySlug>.Fail(RepositorySlugErrors.InvalidFormat);
        }

        string name = parts[^1];
        string owner = string.Join('/', parts[..^1]);

        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) || !AllowedSegmentCharacters().IsMatch(part))
            {
                return Result<RepositorySlug>.Fail(RepositorySlugErrors.InvalidFormat);
            }

            if (part is "." or "..")
            {
                return Result<RepositorySlug>.Fail(RepositorySlugErrors.InvalidFormat);
            }
        }

        return new RepositorySlug(owner, name);
    }

    public override string ToString() => $"{Owner}/{Name}";

    [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
    private static partial Regex AllowedSegmentCharacters();
}
