using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.Core.Results;

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
    /// Reads the offset a cursor names. A missing cursor starts at the first row. A cursor that is malformed, for
    /// another scope, or past <paramref name="total"/> is invalid input at <c>cursor</c>; a cursor for this scope
    /// whose checksum no longer matches is a stale-cursor conflict.
    /// </summary>
    internal static OperationResult<int> Decode(string? cursor, string scope, string checksum, int? total = null)
    {
        if (cursor is null)
        {
            return new OperationSuccess<int>(0);
        }

        CursorPayload? payload = Parse(cursor);
        if (payload is null || payload.Offset < 0 || payload.Offset > (total ?? int.MaxValue) ||
            !FixedTimeEquals(payload.Scope, scope))
        {
            return Invalid();
        }

        return FixedTimeEquals(payload.Checksum, checksum)
            ? new OperationSuccess<int>(payload.Offset)
            : Stale();
    }

    /// <summary>
    /// Reads the offset like <see cref="Decode"/>, and on failure supplies the refusal as a result of the caller's
    /// type.
    /// </summary>
    internal static bool TryRead<T>(
        string? cursor,
        string scope,
        string checksum,
        int? total,
        out int offset,
        out OperationResult<T> failure)
    {
        OperationResult<int> decoded = Decode(cursor, scope, checksum, total);
        if (decoded is OperationSuccess<int> position)
        {
            offset = position.Data;
            failure = default;
            return true;
        }

        offset = 0;
        failure = OperationFailure.Forward<int, T>(decoded);
        return false;
    }

    /// <summary>
    /// Builds the failure for a cursor that cannot be used with this request.
    /// </summary>
    internal static OperationInvalidInput Invalid()
    {
        return new OperationInvalidInput(
            SharedReasonCodes.InvalidCursor,
            "The cursor is not valid for this request. Pass nextCursor unchanged with the same arguments, or start " +
            "again without a cursor.",
            "cursor");
    }

    /// <summary>
    /// Builds the conflict for a cursor whose data changed since the first page.
    /// </summary>
    internal static OperationConflict Stale()
    {
        return new OperationConflict(
            SharedReasonCodes.CursorStale,
            "The data behind this cursor changed since the first page. Start again without a cursor.",
            null);
    }

    /// <summary>
    /// Reads the payload of an opaque cursor, or null when it is not one this class wrote.
    /// </summary>
    private static CursorPayload? Parse(string cursor)
    {
        try
        {
            string padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            CursorPayload? payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(padded));
            return payload is { Scope: not null, Checksum: not null } ? payload : null;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            return null;
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
