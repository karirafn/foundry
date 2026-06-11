using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

internal static class AccountErrors
{
    internal const string NotFoundCode = "Account.NotFound";
    internal const string DuplicateNameCode = "Account.DuplicateName";
    internal const string InvalidTokenCode = "Account.InvalidToken";

    internal static Error NotFound(AccountId id) =>
        new(NotFoundCode, $"Account with ID '{id.Value}' was not found.");

    internal static Error DuplicateName(string name) =>
        new(DuplicateNameCode, $"An account named '{name}' already exists.");

    internal static readonly Error InvalidToken =
        new(InvalidTokenCode, "The token is not valid or is missing required scopes.");
}
