namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// What a run that stopped to ask a person left behind (ADR 0096): its questions, and whether its branch carries
/// commits. Together with the event's pull request URL these are the facts the resume target is derived from.
/// </summary>
public sealed record WorkerInputRequest(IReadOnlyList<InputQuestion> Questions, bool BranchHasCommits);

/// <summary>
/// One question a run asks a person, with the context the person needs to answer it and the one option the run
/// recommends, with its reason. A reply that leaves the question unaddressed accepts the recommendation.
/// <paramref name="Recommendation"/> is null only for a question stored before runs returned recommendations.
/// </summary>
public sealed record InputQuestion(string Question, string Context, string? Recommendation);
