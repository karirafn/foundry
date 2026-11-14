namespace Foundry.Shared;

public enum ErrorKind
{
    Unknown,
    NotFound,

    /// <summary>The provider refused the request's credentials (HTTP 401).</summary>
    Unauthorized,
}
