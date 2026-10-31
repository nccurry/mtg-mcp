using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.CardLists;

/// <summary>
/// Carries what a page cursor holds: the fingerprint of the request, the position of the next item, and the marker
/// of each source, so a source that changed between pages is noticed. Nothing else is stored.
/// </summary>
/// <param name="Fingerprint">The fingerprint of the request without its cursor and page size.</param>
/// <param name="Offset">The position of the first item of the next page.</param>
/// <param name="Markers">The marker of each source, in request order.</param>
internal sealed record CardListCursor(string? Fingerprint, int Offset, IReadOnlyList<string>? Markers);

/// <summary>
/// Builds request fingerprints and reads and writes page cursors for the card list tools.
/// </summary>
internal static class CardListCursors
{
    /// <summary>
    /// Fingerprints the parts of a request that must stay the same from page to page. The parts are serialized with
    /// the server's own options, in the order given.
    /// </summary>
    internal static string Fingerprint(params (object? Value, Type Type)[] parts)
    {
        StringBuilder text = new();
        foreach ((object? value, Type type) in parts)
        {
            text.Append(JsonSerializer.Serialize(value, type, ToolCallPolicy.SerializerOptions)).Append('\u001f');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..32];
    }

    /// <summary>
    /// Encodes a cursor as opaque text.
    /// </summary>
    internal static string Encode(string fingerprint, int offset, IReadOnlyList<string> markers)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new CardListCursor(fingerprint, offset, markers));
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Reads a cursor and checks that it belongs to this request. A missing cursor starts at the first item. A cursor
    /// that is malformed or belongs to another request is invalid input and returns no data.
    /// </summary>
    internal static OperationResult<CardListCursor> Read(string? cursor, string fingerprint)
    {
        if (cursor is null)
        {
            return new OperationSuccess<CardListCursor>(new CardListCursor(fingerprint, 0, null));
        }

        CardListCursor? decoded = Decode(cursor);
        if (decoded?.Fingerprint is null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(decoded.Fingerprint),
                Encoding.UTF8.GetBytes(fingerprint)) ||
            decoded.Offset < 0 ||
            decoded.Markers is null)
        {
            return new OperationInvalidInput(
                "invalid-cursor",
                "The cursor is not valid for this request. Send the same request with nextCursor unchanged, or " +
                "start again without a cursor.",
                "cursor");
        }

        return new OperationSuccess<CardListCursor>(decoded);
    }

    /// <summary>
    /// Compares the sources read now with the markers the cursor stored. A source that changed returns a conflict
    /// that carries its new revision or state marker, so the agent can start again.
    /// </summary>
    internal static OperationConflict? FindChange(CardListCursor cursor, IReadOnlyList<CardListSourceInfo> sources)
    {
        if (cursor.Markers is not { } stored)
        {
            return null;
        }

        for (int index = 0; index < sources.Count; index++)
        {
            string now = sources[index].Marker;
            if (index < stored.Count && string.Equals(stored[index], now, StringComparison.Ordinal))
            {
                continue;
            }

            ConflictState? state = sources[index] switch
            {
                { Kind: "deck", Revision: long revision } => new ConflictState(Revision: revision),
                { Kind: "archidekt-deck", StateMarker: { } marker } => new ConflictState(RemoteFingerprint: marker),
                _ => null,
            };
            return new OperationConflict(
                "source-changed-between-pages",
                $"The source at {sources[index].Side} changed since the first page. Start again without a cursor.",
                state);
        }

        return null;
    }

    /// <summary>
    /// Decodes a cursor, or returns null when it is malformed.
    /// </summary>
    private static CardListCursor? Decode(string cursor)
    {
        try
        {
            string padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            return JsonSerializer.Deserialize<CardListCursor>(Convert.FromBase64String(padded));
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
