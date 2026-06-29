using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryErrors
{
    internal const string NotFoundCode = "Repository.NotFound";
    internal const string DuplicateSlugCode = "Repository.DuplicateSlug";
    internal const string AccountNotFoundCode = "Repository.AccountNotFound";
    internal const string AccountHasNoTokenCode = "Repository.AccountHasNoToken";
    internal const string NoTokenCode = "Repository.NoToken";
    internal const string ConflictOnCreateCode = "Repository.ConflictOnCreate";

    internal static Error NotFound(MonitoredRepositoryId id) =>
        new(NotFoundCode, $"Repository with ID '{id.Value}' was not found.");

    internal static Error DuplicateSlug(string slug) =>
        new(DuplicateSlugCode, $"A repository with slug '{slug}' already exists.");

    internal static Error AccountNotFound(AccountId id) =>
        new(AccountNotFoundCode, $"Account with ID '{id.Value}' was not found.");

    internal static Error AccountHasNoToken(AccountId id) =>
        new(AccountHasNoTokenCode, $"Account with ID '{id.Value}' has no token configured.");

    internal static Error NoToken(AccountId id) =>
        new(NoTokenCode, $"Account with ID '{id.Value}' has no token — eligibility cannot be re-checked.");

    internal static Error ConflictOnCreate() =>
        new(ConflictOnCreateCode, "The repository could not be created due to a conflict. Please try again.");
}
