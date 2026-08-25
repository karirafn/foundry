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

    public static readonly Error ContainsQueryOrFragment = new(
        "BaseUrl.Invalid",
        "Base URL must not contain a query string or fragment.");

    public static readonly Error PrivateHost = new(
        "BaseUrl.PrivateHost",
        "Base URL must not use a literal IP address as host. Provider base URLs are always DNS-named.");
}
