using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.UnitTests.Modules.Monitoring;

internal static class BaseUrlFactory
{
    internal static BaseUrl Create(string url) =>
        ((Result<BaseUrl>.Success)BaseUrl.Create(url)).Value;
}
