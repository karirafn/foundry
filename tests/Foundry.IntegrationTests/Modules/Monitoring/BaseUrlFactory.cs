using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.IntegrationTests.Modules.Monitoring;

internal static class BaseUrlFactory
{
    internal static BaseUrl Create(string url) =>
        BaseUrl.Create(url).ValueOrThrow();
}
