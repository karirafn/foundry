using System.Globalization;
using System.Text;

using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Features;

/// <summary>
/// Encodes and decodes keyset pagination cursors for the resolved issue query.
/// A cursor is a base64url-encoded string of the form "{detectedAt:O}|{id:D}".
///
/// Contract: <see cref="GetResolvedIssueSummariesAsync"/> callers are responsible
/// for validating the cursor via <see cref="Decode"/> before calling the query.
/// The query assumes a non-null cursor is well-formed.
/// The endpoint (step 6) decodes the cursor, returns 400 on <see cref="IssueErrors.InvalidCursor"/>
/// from <see cref="Decode"/>, and passes <see langword="null"/> or the raw string to the query.
/// </summary>
internal static class IssueCursor
{
    private const char Separator = '|';

    /// <summary>
    /// Encodes a keyset cursor from the last returned row's <paramref name="detectedAt"/>
    /// and <paramref name="id"/>. Uses the same UTC ISO-8601 round-trip format ("O") that
    /// the database column uses so decoded values compare identically against stored values.
    /// </summary>
    public static string Encode(DateTimeOffset detectedAt, IssueId id)
    {
        string payload = $"{detectedAt.UtcDateTime.ToString("O")}{Separator}{id.Value:D}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Decodes a cursor produced by <see cref="Encode"/>.
    /// Returns <see cref="Result{T}.Failure"/> with <see cref="IssueErrors.InvalidCursor"/>
    /// when the cursor is malformed — never throws across the call boundary.
    /// </summary>
    public static Result<(DateTimeOffset DetectedAt, IssueId Id)> Decode(string cursor)
    {
        try
        {
            byte[] bytes = Base64UrlDecode(cursor);
            string payload = Encoding.UTF8.GetString(bytes);

            int separatorIndex = payload.IndexOf(Separator, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return Result<(DateTimeOffset, IssueId)>.Fail(IssueErrors.InvalidCursor());
            }

            string detectedAtPart = payload[..separatorIndex];
            string idPart = payload[(separatorIndex + 1)..];

            if (!DateTimeOffset.TryParseExact(
                    detectedAtPart,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset detectedAt))
            {
                return Result<(DateTimeOffset, IssueId)>.Fail(IssueErrors.InvalidCursor());
            }

            if (!Guid.TryParseExact(idPart, "D", out Guid guid))
            {
                return Result<(DateTimeOffset, IssueId)>.Fail(IssueErrors.InvalidCursor());
            }

            return Result<(DateTimeOffset, IssueId)>.Ok((detectedAt, IssueId.From(guid)));
        }
        catch (FormatException)
        {
            return Result<(DateTimeOffset, IssueId)>.Fail(IssueErrors.InvalidCursor());
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string encoded)
    {
        string base64 = encoded
            .Replace('-', '+')
            .Replace('_', '/');

        int padding = base64.Length % 4;
        if (padding > 0)
        {
            base64 += new string('=', 4 - padding);
        }

        return Convert.FromBase64String(base64);
    }
}
