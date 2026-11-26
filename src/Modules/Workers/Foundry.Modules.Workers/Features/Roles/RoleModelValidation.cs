using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.Roles.Models;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>
/// The rule a saved model setting follows, for a role's model and a Tier Model alike: empty (the CLI's account
/// default), a Claude Code alias, or a model ID the Models API lists, at most <see cref="WorkerRoleSettings.MaxModelLength"/>
/// characters.
/// </summary>
internal static class RoleModelValidation
{
    internal const string ModelNotListedCode = "Worker.RoleModelNotListed";
    internal const string ModelTooLongCode = "Worker.RoleModelTooLong";

    internal static readonly Error ModelTooLong =
        new(ModelTooLongCode, $"The model must not exceed {WorkerRoleSettings.MaxModelLength} characters.");

    internal static Error ModelNotListed(string model, string? refreshError) =>
        new(
            ModelNotListedCode,
            refreshError is null
                ? $"'{model}' is not a Claude Code alias or a model listed by the Models API. Pick one from the list."
                : $"'{model}' is not a Claude Code alias, and the model list could not be refreshed: {refreshError}");

    /// <returns>The trimmed model, or why it cannot be saved.</returns>
    internal static async Task<Result<string>> ValidateAsync(
        string model,
        IClaudeModelCatalog modelCatalog,
        CancellationToken cancellationToken)
    {
        string trimmed = model.Trim();

        if (trimmed.Length > WorkerRoleSettings.MaxModelLength)
        {
            return Result<string>.Fail(ModelTooLong);
        }

        if (trimmed.Length == 0 || ClaudeModelAliases.IsAlias(trimmed))
        {
            return trimmed;
        }

        // Validated against the current cached list; it is fetched first when it never was.
        ClaudeModelList list = await modelCatalog.GetAsync(cancellationToken);

        return list.Lists(trimmed)
            ? trimmed
            : Result<string>.Fail(ModelNotListed(trimmed, list.RefreshError));
    }
}
