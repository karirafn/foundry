using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.Providers.Feedback;

/// <param name="Id">
/// The provider's comment ID; <see langword="null"/> only when the provider omitted it (GitHub's GraphQL
/// schema declares <c>databaseId</c> nullable), in which case the comment can never match the
/// posted-comment registry.
/// </param>
internal sealed record ProviderComment(
    ProviderCommentId? Id,
    string Body,
    string AuthorLogin,
    bool AuthorIsBot,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    string? FilePath,
    int? Line,
    CommentOrigin Origin,
    bool ThreadResolved);
