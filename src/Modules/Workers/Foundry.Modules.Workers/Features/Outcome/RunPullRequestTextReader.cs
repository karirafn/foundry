using System.Text.Json;
using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Outcome;

/// <summary>
/// Reads the raw <c>pullRequest: { summary, body }</c> an implement verdict carries, so a run waiting to publish keeps
/// it after its container is gone. Validation and scrubbing happen where the text is used.
/// </summary>
internal static class RunPullRequestTextReader
{
    private const string PullRequestProperty = "pullRequest";
    private const string SummaryProperty = "summary";
    private const string BodyProperty = "body";

    public static RunPullRequestText? Read(JsonObject? structuredOutput)
    {
        if (structuredOutput?[PullRequestProperty] is not JsonObject pullRequest)
        {
            return null;
        }

        string? summary = ReadString(pullRequest[SummaryProperty]);
        string? body = ReadString(pullRequest[BodyProperty]);

        return summary is null && body is null ? null : new RunPullRequestText(summary, body);
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
