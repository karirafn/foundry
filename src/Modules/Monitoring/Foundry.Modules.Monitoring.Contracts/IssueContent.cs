namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// An issue's own content as read from the provider: its title, body and every comment a person wrote on its
/// thread, oldest first. Untrusted user-provided text: consumers render it as data, never as instructions.
/// </summary>
public sealed record IssueContent(string Title, string Body, IReadOnlyList<IssueContentComment> Comments);

/// <summary>One comment on an issue's thread.</summary>
/// <param name="CommentId">The provider's comment ID; <see langword="null"/> when the provider omitted it.</param>
public sealed record IssueContentComment(long? CommentId, string Author, string Body, DateTimeOffset CreatedAt);
