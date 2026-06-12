namespace Foundry.Modules.Settings.Contracts;

public sealed record AuthValidationResult
{
    public bool IsValid { get; init; }
    public bool PassedOptimistically { get; init; }
    public string? ErrorMessage { get; init; }

    private AuthValidationResult() { }

    public static AuthValidationResult Valid() =>
        new() { IsValid = true };

    public static AuthValidationResult ValidOptimistic() =>
        new() { IsValid = true, PassedOptimistically = true };

    public static AuthValidationResult Invalid(string message) =>
        new() { IsValid = false, ErrorMessage = message };
}
