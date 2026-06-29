namespace Foundry.Modules.Workers.Domain;

/// <summary>
/// Parsed summary from the final JSON result line emitted by a claude --output-format json run.
/// </summary>
public sealed record RunResultSummary
{
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
        return new RunResultSummary(
            resultText,
            subtype,
            isError,
            durationMs,
            numTurns,
            totalCostUsd,
            inputTokens,
            outputTokens);
    }
}
