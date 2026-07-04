using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Scryfall;

/// <summary>
/// Creates checksum-bound opaque cursors for immutable local result pages.
/// </summary>
internal static class ScryfallCursor
{
    /// <summary>
    /// Identifies the authored collection cursor representation.
    /// </summary>
    private const int CollectionCursorSchemaVersion = 1;

    /// <summary>
    /// Encodes one immutable collection identity and next ordinal.
    /// </summary>
    internal static string Encode(string scope, string checksum, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksum);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(scope, checksum, offset));
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes one cursor only when its scope and checksum match the requested evidence.
    /// </summary>
    internal static bool TryDecode(
        string? cursor,
        string scope,
        string checksum,
        out int offset)
    {
        offset = 0;
        if (cursor is null)
        {
            return true;
        }

        try
        {
            string padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            CursorPayload? payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(padded));
            if (payload is null || payload.Offset < 0)
            {
                return false;
            }

            bool scopeMatches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(payload.Scope),
                Encoding.UTF8.GetBytes(scope));
            bool checksumMatches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(payload.Checksum),
                Encoding.UTF8.GetBytes(checksum));
            if (!scopeMatches || !checksumMatches)
            {
                return false;
            }

            offset = payload.Offset;
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encodes the exact evidence identities needed to continue one collection page without acquisition.
    /// </summary>
    internal static string EncodeCollection(ScryfallCollectionCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.RequestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ResultChecksum);
        ArgumentOutOfRangeException.ThrowIfNegative(state.Offset);
        if (state.SnapshotId.HasValue != !string.IsNullOrWhiteSpace(state.SnapshotChecksum) ||
            state.MissStatus is not ("not-cached" or "not-found"))
        {
            throw new ArgumentException("Collection cursor evidence is inconsistent.", nameof(state));
        }

        ScryfallCollectionCursorState authored = state with { SchemaVersion = CollectionCursorSchemaVersion };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(authored);
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes collection continuation state only when its schema and ordered input fingerprint match.
    /// </summary>
    internal static bool TryDecodeCollection(
        string cursor,
        string expectedRequestHash,
        out ScryfallCollectionCursorState? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(cursor) || string.IsNullOrWhiteSpace(expectedRequestHash))
        {
            return false;
        }

        try
        {
            string padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            ScryfallCollectionCursorState? payload =
                JsonSerializer.Deserialize<ScryfallCollectionCursorState>(Convert.FromBase64String(padded));
            if (payload is null ||
                payload.SchemaVersion != CollectionCursorSchemaVersion ||
                payload.Offset < 0 ||
                string.IsNullOrWhiteSpace(payload.ResultChecksum) ||
                payload.SnapshotId.HasValue != !string.IsNullOrWhiteSpace(payload.SnapshotChecksum) ||
                payload.MissStatus is not ("not-cached" or "not-found") ||
                !FixedTimeEquals(payload.RequestHash, expectedRequestHash))
            {
                return false;
            }

            state = payload;
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Compares cursor-bound text without leaking partial-match timing.
    /// </summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
    }

    /// <summary>
    /// Carries the authenticated-by-comparison cursor fields before opaque encoding.
    /// </summary>
    private sealed record CursorPayload(string Scope, string Checksum, int Offset);
}

/// <summary>
/// Carries verified collection continuation evidence before it is projected into a bounded page.
/// </summary>
internal sealed record ScryfallCollectionCursorState(
    int SchemaVersion,
    string RequestHash,
    Guid? CorpusGenerationId,
    Guid? SnapshotId,
    string? SnapshotChecksum,
    string ResultChecksum,
    string MissStatus,
    int Offset);
