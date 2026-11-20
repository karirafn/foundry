namespace Foundry.Shared;

/// <summary>
/// A failure's code and message. Not sealed so that a failure the caller must render with structured data (such as the
/// issues blocking a removal) can carry that data on a derived record.
/// </summary>
public record Error(string Code, string Message)
{
    public ErrorKind Kind { get; init; } = ErrorKind.Unknown;
}
