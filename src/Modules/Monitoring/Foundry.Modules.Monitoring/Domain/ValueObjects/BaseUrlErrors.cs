using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

public static class BaseUrlErrors
{
    public static readonly Error Invalid = new(
        "BaseUrl.Invalid",
        "Base URL must be a valid HTTPS URL.");

    public static readonly Error ContainsCredentials = new(
        "BaseUrl.ContainsCredentials",
        "Base URL must not contain user credentials.");
}
