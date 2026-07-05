using Foundry.Shared;

namespace Foundry.Modules.Credentials.Domain;

public sealed class ClaudeAccount : AggregateRoot<ClaudeAccountId>
{
    private ClaudeAccount() : base(ClaudeAccountId.Default)
    {
    }

    private ClaudeAccount(ClaudeAccountId id, DateTimeOffset createdAt) : base(id)
    {
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ClaudeAccount Create()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ClaudeAccount(ClaudeAccountId.Default, now);
    }
}
