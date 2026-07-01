namespace Foundry.Shared;

public sealed record Error(string Code, string Message)
{
    public ErrorKind Kind { get; init; } = ErrorKind.Unknown;
}
