namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// Parsed summary from the final JSON result line emitted by a claude --output-format json run.
/// </summary>
public sealed record RunResultSummary
{
    public const int ResultTextMaxLength = 100;
    public const int SubtypeMaxLength = 50;

    public string? ResultText { get; }
    public string? Subtype { get; }
    public bool IsError { get; }
    public long DurationMs { get; }
    public int NumTurns { get; }
    public decimal? TotalCostUsd { get; }
    public int? InputTokens { get; }
    public int? OutputTokens { get; }

    private RunResultSummary(
        string? resultText,
        string? subtype,
        bool isError,
        long durationMs,
        int numTurns,
        decimal? totalCostUsd,
        int? inputTokens,
        int? outputTokens)
    {
        ResultText = resultText;
        Subtype = subtype;
        IsError = isError;
        DurationMs = durationMs;
        NumTurns = numTurns;
        TotalCostUsd = totalCostUsd;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    public static RunResultSummary Create(
        string? resultText,
        string? subtype,
        bool isError,
        long durationMs,
        int numTurns,
        decimal? totalCostUsd,
        int? inputTokens,
        int? outputTokens)
    {
        // The Claude CLI emits subtype:"success" even when is_error:true — normalize the
        // misleading value to null so a failed run never surfaces a "success" subtype.
        // Genuine error subtypes (e.g. "error_max_turns") are preserved unchanged.
        string? normalizedSubtype = isError && string.Equals(subtype, "success", StringComparison.OrdinalIgnoreCase)
            ? null
            : subtype;

        return new RunResultSummary(
            resultText,
            normalizedSubtype,
            isError,
            durationMs,
            numTurns,
            totalCostUsd,
            inputTokens,
            outputTokens);
    }
}
