using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// The provider's own ID of a comment: a GitHub issue-comment, review-comment or review database ID, or a
/// GitLab note ID. Unique within one repository's comment surface, never across providers.
/// </summary>
public readonly record struct ProviderCommentId
{
    private ProviderCommentId(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public static Result<ProviderCommentId> Create(long value) =>
        value > 0
            ? Result<ProviderCommentId>.Ok(new ProviderCommentId(value))
            : Result<ProviderCommentId>.Fail(ProviderCommentIdErrors.NotPositive(value));

    /// <summary>
    /// Maps a raw ID from a provider response; an absent or invalid ID yields <see langword="null"/>, so
    /// a malformed comment is still read but can never match a registry entry.
    /// </summary>
    public static ProviderCommentId? FromProvider(long? value) =>
        value is { } raw && Create(raw) is Result<ProviderCommentId>.Success success ? success.Value : null;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class ProviderCommentIdErrors
{
    public const string NotPositiveCode = "ProviderCommentId.NotPositive";

    public static Error NotPositive(long value) =>
        new(NotPositiveCode, $"A provider comment ID must be positive, but was {value}.");
}
