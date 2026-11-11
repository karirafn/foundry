using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Testing;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;

internal sealed class ProviderCommentBuilder
{
    private static readonly DateTimeOffset DefaultCreatedAt = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private ProviderCommentId? _id = ProviderCommentId.Create(1).ValueOrThrow();
    private string _body = "Please fix this.";
    private string _authorLogin = "octocat";
    private bool _authorIsBot;
    private bool _isSystem;
    private DateTimeOffset _createdAt = DefaultCreatedAt;
    private string? _filePath;
    private int? _line;
    private CommentOrigin _origin = CommentOrigin.Conversation;
    private bool _threadResolved;

    public ProviderCommentBuilder WithId(long? value) { _id = value is { } raw ? ProviderCommentId.Create(raw).ValueOrThrow() : null; return this; }

    public ProviderCommentBuilder WithBody(string value) { _body = value; return this; }

    public ProviderCommentBuilder WithAuthorLogin(string value) { _authorLogin = value; return this; }

    public ProviderCommentBuilder WithAuthorIsBot(bool value) { _authorIsBot = value; return this; }

    public ProviderCommentBuilder WithIsSystem(bool value) { _isSystem = value; return this; }

    public ProviderCommentBuilder WithCreatedAt(DateTimeOffset value) { _createdAt = value; return this; }

    public ProviderCommentBuilder WithFilePath(string? value) { _filePath = value; return this; }

    public ProviderCommentBuilder WithLine(int? value) { _line = value; return this; }

    public ProviderCommentBuilder WithOrigin(CommentOrigin value) { _origin = value; return this; }

    public ProviderCommentBuilder WithThreadResolved(bool value) { _threadResolved = value; return this; }

    public ProviderComment Build() =>
        new(
            Id: _id,
            Body: _body,
            AuthorLogin: _authorLogin,
            AuthorIsBot: _authorIsBot,
            IsSystem: _isSystem,
            CreatedAt: _createdAt,
            FilePath: _filePath,
            Line: _line,
            Origin: _origin,
            ThreadResolved: _threadResolved);
}
