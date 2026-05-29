using System.Text.RegularExpressions;

using Foundry.Shared;

namespace Foundry.WebApi.Modules.Monitoring.Domain;

public sealed partial record RepositorySlug
{
    public string Owner { get; }

    public string Name { get; }

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

        if (parts.Length != 2)
        {
            return Result<RepositorySlug>.Fail(RepositorySlugErrors.InvalidFormat);
        }

        string owner = parts[0];
        string name = parts[1];

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
        {
            return Result<RepositorySlug>.Fail(RepositorySlugErrors.InvalidFormat);
        }

        if (!AllowedSegmentCharacters().IsMatch(owner) || !AllowedSegmentCharacters().IsMatch(name))
        {
            return Result<RepositorySlug>.Fail(RepositorySlugErrors.InvalidFormat);
        }

        return new RepositorySlug(owner, name);
    }

    public override string ToString() => $"{Owner}/{Name}";

    [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$")]
    private static partial Regex AllowedSegmentCharacters();
}

public static class RepositorySlugErrors
{
    public static readonly Error InvalidFormat = new(
        "RepositorySlug.InvalidFormat",
        "Repository slug must be in the format 'owner/name' with non-empty segments.");
}
