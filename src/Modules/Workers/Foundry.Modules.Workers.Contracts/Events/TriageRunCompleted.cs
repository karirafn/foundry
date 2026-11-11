using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// A triage run exited 0 with a schema-valid verdict (ADR 0097). Published instead of <see cref="WorkerRunCompleted"/>:
/// a triage run leaves no branch or merge request, only its <paramref name="Result"/>. A triage run without a valid
/// verdict fails through <see cref="WorkerRunFailed"/> instead.
/// </summary>
/// <param name="DecidedAt">When Foundry read the verdict.</param>
public sealed record TriageRunCompleted(
    Guid IssueId,
    WorkerRunId WorkerRunId,
    TriageResult Result,
    DateTimeOffset DecidedAt) : IIntegrationEvent;

/// <summary>What kind of change triage judged an issue to ask for.</summary>
public enum TriageKind
{
    Feature,
    Bug,
    Refactor,
    Documentation,
}

/// <summary>A triage run's verdict: <see cref="Ready"/> to implement as written, or <see cref="NeedsInput"/> first.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Ready), "ready")]
[JsonDerivedType(typeof(NeedsInput), "needs_input")]
public abstract record TriageResult
{
    private TriageResult()
    {
    }

    public sealed record Ready(TriageKind Kind) : TriageResult;

    public sealed record NeedsInput(TriageKind Kind, IReadOnlyList<InputQuestion> Questions) : TriageResult;
}
