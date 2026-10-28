namespace Foundry.Shared;

/// <summary>
/// A single provider check that failed at the time the outcome was recorded.
/// </summary>
public sealed record FailingCheck(string Name, string Conclusion, string? DetailsUrl);
