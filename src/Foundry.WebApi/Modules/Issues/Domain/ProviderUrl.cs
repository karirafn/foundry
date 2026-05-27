using Foundry.WebApi.Shared.Abstractions;

namespace Foundry.WebApi.Modules.Issues.Domain;

public sealed record ProviderUrl
{
    public Uri Value { get; }

    private ProviderUrl(Uri value) => Value = value;

    public static Result<ProviderUrl> Create(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return Result<ProviderUrl>.Fail(ProviderUrlErrors.InvalidUrl);
        }

        return new ProviderUrl(uri);
    }

    public override string ToString() => Value.ToString();
}

public static class ProviderUrlErrors
{
    public static readonly Error InvalidUrl = new("ProviderUrl.InvalidUrl", "URL must be a valid absolute URI.");
}
