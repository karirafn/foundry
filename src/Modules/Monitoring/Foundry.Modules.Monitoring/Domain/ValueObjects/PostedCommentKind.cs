namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>Which provider ID a posted-comment registry entry holds, and so which comments it matches.</summary>
public enum PostedCommentKind
{
    /// <summary>A conversation comment's own ID: a GitHub issue comment or a GitLab note.</summary>
    Conversation,

    /// <summary>A GitHub review's ID: matches the review's summary and every comment the review posted.</summary>
    Review,

    /// <summary>A GitLab diff note's own ID: matches only that note, never a reply in its thread.</summary>
    ReviewNote,
}
