namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// How an issue's <see cref="IssueKind"/> was decided. Closed: a kind label decided it
/// (<see cref="Labelled"/>), or nothing has yet (<see cref="Unclassified"/>).
/// </summary>
public abstract record KindClassification
{
    public static readonly KindClassification Unclassified = new UnclassifiedKind();

    private KindClassification()
    {
    }

    public sealed record Labelled(IssueKind Kind) : KindClassification;

    private sealed record UnclassifiedKind : KindClassification;

    /// <summary>
    /// Maps the kind label Monitoring classified from the issue's labels. A missing or
    /// unrecognized label means no label decided the kind.
    /// </summary>
    public static KindClassification FromKindLabel(string? kindLabel) =>
        kindLabel is not null && IssueKind.FromLabel(kindLabel) is IssueKind kind
            ? new Labelled(kind)
            : Unclassified;
}
