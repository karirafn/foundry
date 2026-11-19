using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>A comment on line <see cref="Line"/> of <see cref="Path"/> in a pull/merge request's head version.</summary>
internal sealed record InlineComment(string Path, int Line, string Body);

/// <summary>An inline comment the provider created, with the registry key that keeps it out of review feedback.</summary>
internal sealed record PostedInlineComment(InlineComment Comment, PostedCommentKey Key);

/// <summary>An inline comment the provider did not create, and why.</summary>
internal sealed record FailedInlineComment(InlineComment Comment, Error Error);

/// <summary>What an inline review post created. A provider that posts comments one by one may create only some.</summary>
internal sealed record InlineReviewPosting(IReadOnlyList<PostedInlineComment> Posted, IReadOnlyList<FailedInlineComment> Failed);
