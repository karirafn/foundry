using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

public sealed record BaseUrl
{
    public Uri Value { get; }

    private BaseUrl(Uri value) => Value = value;

    public static Result<BaseUrl> Create(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return Result<BaseUrl>.Fail(BaseUrlErrors.Invalid);
        }

        // uri.UserInfo is "" for https://@host — the @ is preserved in AbsoluteUri but stripped from UserInfo/Authority,
        // so we must also check the raw URL string for an @ in the authority position.
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsoluteUri.Contains('@', StringComparison.Ordinal))
        {
            return Result<BaseUrl>.Fail(BaseUrlErrors.ContainsCredentials);
        }

        return new BaseUrl(uri);
    }

    public static BaseUrl FromPersistedString(string value)
    {
        Result<BaseUrl> result = Create(value);

        return result switch
        {
            Result<BaseUrl>.Success s => s.Value,
            _ => throw new InvalidOperationException(
                $"Persisted base_url '{value}' failed validation: " +
                ((Result<BaseUrl>.Failure)result).Error.Message),
        };
    }

    public override string ToString() => Value.ToString();
}
