namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// Parsed summary from the last "result" event emitted by a claude --output-format stream-json run.
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

    /// <summary>Uncached input tokens. Not billable input on its own — see <see cref="Tokens"/>.</summary>
    public int? InputTokens { get; }
    public int? CacheReadInputTokens { get; }
    public int? CacheCreationInputTokens { get; }
    public int? OutputTokens { get; }

    /// <summary>The persisted token buckets as a <see cref="TokenUsage"/>.</summary>
    public TokenUsage Tokens => new(InputTokens, CacheReadInputTokens, CacheCreationInputTokens, OutputTokens);

    private RunResultSummary(
        string? resultText,
        string? subtype,
        bool isError,
        long durationMs,
        int numTurns,
        decimal? totalCostUsd,
        int? inputTokens,
        int? cacheReadInputTokens,
        int? cacheCreationInputTokens,
        int? outputTokens)
    {
        ResultText = resultText;
        Subtype = subtype;
        IsError = isError;
        DurationMs = durationMs;
        NumTurns = numTurns;
        TotalCostUsd = totalCostUsd;
        InputTokens = inputTokens;
        CacheReadInputTokens = cacheReadInputTokens;
        CacheCreationInputTokens = cacheCreationInputTokens;
        OutputTokens = outputTokens;
    }

    public static RunResultSummary Create(
        string? resultText,
        string? subtype,
        bool isError,
        long durationMs,
        int numTurns,
        decimal? totalCostUsd,
        TokenUsage tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

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
            ToStoredCount(tokens.InputTokens),
            ToStoredCount(tokens.CacheReadInputTokens),
            ToStoredCount(tokens.CacheCreationInputTokens),
            ToStoredCount(tokens.OutputTokens));
    }

    // Token columns are INTEGER mapped to int?; a count that does not fit is stored as unknown
    // rather than truncated, so it can never be reported as a wrong figure.
    private static int? ToStoredCount(long? value)
    {
        return value is >= 0 and <= int.MaxValue ? (int)value.Value : null;
    }
}
