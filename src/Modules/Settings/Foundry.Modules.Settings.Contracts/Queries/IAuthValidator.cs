namespace Foundry.Modules.Settings.Contracts.Queries;

public interface IAuthValidator
{
    Task<AuthValidationResult> ValidateAsync(CancellationToken cancellationToken);
}
