using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Repositories;

internal static class RepositoryErrors
{
    internal const string NotFoundCode = "Repository.NotFound";
    internal const string DuplicateSlugCode = "Repository.DuplicateSlug";
    internal const string AccountNotFoundCode = "Repository.AccountNotFound";
    internal const string AccountHasNoTokenCode = "Repository.AccountHasNoToken";
    internal const string AccountTokenUnreadableCode = "Repository.AccountTokenUnreadable";
    internal const string NoTokenCode = "Repository.NoToken";
    internal const string ConflictOnCreateCode = "Repository.ConflictOnCreate";

    internal const string InvalidMaxConcurrentWorkersCode =
        MonitoredRepositoryErrors.InvalidMaxConcurrentWorkersCode;

    internal const string PollIntervalNotPositiveCode =
        MonitoredRepositoryErrors.PollIntervalNotPositiveCode;

    internal const string PollIntervalTooLargeCode =
        MonitoredRepositoryErrors.PollIntervalTooLargeCode;

    internal static Error NotFound(MonitoredRepositoryId id) =>
        new(NotFoundCode, $"Repository with ID '{id.Value}' was not found.");

    internal static Error DuplicateSlug(string slug) =>
        new(DuplicateSlugCode, $"A repository with slug '{slug}' already exists.");

    internal static Error AccountNotFound(CredentialId id) =>
        new(AccountNotFoundCode, $"Account with ID '{id.Value}' was not found.");

    internal static Error AccountHasNoToken(CredentialId id) =>
        new(AccountHasNoTokenCode, $"Account with ID '{id.Value}' has no token configured.");

    internal static Error AccountTokenUnreadable(CredentialId id) =>
        new(AccountTokenUnreadableCode,
            $"Account with ID '{id.Value}' has an unreadable token — the stored ciphertext could not be decrypted.");

    internal static Error NoToken(CredentialId id) =>
        new(NoTokenCode, $"Account with ID '{id.Value}' has no token — eligibility cannot be re-checked.");

    internal static Error ConflictOnCreate() =>
        new(ConflictOnCreateCode, "The repository could not be created due to a conflict. Please try again.");
}
