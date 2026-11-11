namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// How an issue's <see cref="IssueKind"/> was decided. Closed: a kind label decided it (<see cref="Labelled"/>),
/// triage judged it while no label decided it (<see cref="Triaged"/>), or nothing has yet (<see cref="Unclassified"/>).
/// A label always beats triage (ADR 0097).
/// </summary>
public abstract record KindClassification
{
    public static readonly KindClassification Unclassified = new UnclassifiedKind();

    private KindClassification()
    {
    }

    public sealed record Labelled(IssueKind Kind) : KindClassification;

    public sealed record Triaged(IssueKind Kind) : KindClassification;

    private sealed record UnclassifiedKind : KindClassification;

    /// <summary>
    /// Maps the kind label Monitoring classified from the issue's labels. A missing or
    /// unrecognized label means no label decided the kind.
    /// </summary>
    public static KindClassification FromKindLabel(string? kindLabel) =>
        kindLabel is not null && IssueKind.FromLabel(kindLabel) is IssueKind kind
            ? new Labelled(kind)
            : Unclassified;

    /// <summary>
    /// The classification from what each source decided: a <paramref name="labelledKind"/> beats a
    /// <paramref name="triagedKind"/>, and neither is <see cref="Unclassified"/>.
    /// </summary>
    public static KindClassification From(IssueKind? labelledKind, IssueKind? triagedKind) =>
        labelledKind is not null
            ? new Labelled(labelledKind)
            : triagedKind is not null
                ? new Triaged(triagedKind)
                : Unclassified;
}
