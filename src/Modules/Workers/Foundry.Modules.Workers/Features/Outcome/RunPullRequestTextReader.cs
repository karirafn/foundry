using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Verdicts;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Reads the raw <c>pullRequest</c> an implement verdict carries, so a run waiting to publish keeps it after its
/// container is gone. Validation and scrubbing happen where the text is used; a field of the wrong type reads as absent.
/// </summary>
internal static class RunPullRequestTextReader
{
    public static RunPullRequestText? Read(JsonObject? structuredOutput)
    {
        if (structuredOutput?[ImplementVerdict.PullRequestProperty] is not JsonObject pullRequest)
        {
            return null;
        }

        string? summary = ReadString(pullRequest[ImplementVerdict.SummaryProperty]);
        RunPullRequestDescription? description = ReadDescription(pullRequest);

        return summary is null && description is null ? null : new RunPullRequestText(summary, description);
    }

    private static RunPullRequestDescription? ReadDescription(JsonObject pullRequest)
    {
        string? cause = ReadString(pullRequest[ImplementVerdict.CauseProperty]);
        IReadOnlyList<string> changes = ImplementVerdict.ReadStrings(pullRequest[ImplementVerdict.ChangesProperty]) ?? [];
        IReadOnlyList<string> verification = ImplementVerdict.ReadStrings(pullRequest[ImplementVerdict.VerificationProperty]) ?? [];
        IReadOnlyList<string> attention = ImplementVerdict.ReadStrings(pullRequest[ImplementVerdict.AttentionProperty]) ?? [];
        string? notes = ReadString(pullRequest[ImplementVerdict.NotesProperty]);

        bool empty = cause is null && notes is null && changes.Count == 0 && verification.Count == 0 && attention.Count == 0;
        return empty ? null : new RunPullRequestDescription(cause, changes, verification, attention, notes);
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
