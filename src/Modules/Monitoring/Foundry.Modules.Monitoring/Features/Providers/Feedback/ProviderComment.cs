namespace Foundry.Modules.Monitoring.Features.Providers.Feedback;

internal sealed record ProviderComment(
    string Body,
    string AuthorLogin,
    bool AuthorIsBot,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    string? FilePath,
    int? Line,
    CommentOrigin Origin,
    bool ThreadResolved);
