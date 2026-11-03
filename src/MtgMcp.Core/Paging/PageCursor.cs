using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Core.Paging;

/// <summary>
/// Writes and reads the opaque cursor that every paged tool takes and returns. A cursor holds a version, a position
/// shape, the position, and a binding, with a checksum of all of them. The 8-byte unkeyed checksum detects accidental
/// edits and cuts, not deliberate ones: anyone can compute it. Every reader therefore bounds the position it reads
/// against the data it pages, so a forged cursor can only point inside data the caller may read.
/// </summary>
public static class PageCursor
{
    /// <summary>
    /// Limits the length of a cursor in characters, for what is written and for what is read.
    /// </summary>
    public const int MaximumLength = 1024;

    /// <summary>
    /// Names the layout of the payload. A cursor of another version is refused.
    /// </summary>
    private const int Version = 1;

    /// <summary>
    /// Counts the checksum bytes kept after the payload.
    /// </summary>
    private const int ChecksumBytes = 8;

    /// <summary>
    /// Separates the payload from its checksum, a character that URL-safe base64 never uses.
    /// </summary>
    private const char Separator = '.';

    /// <summary>
    /// Writes the cursor for the page that starts at a position, bound to the state the page was read from.
    /// </summary>
    /// <exception cref="ArgumentException">A position is negative or the cursor would pass the length cap.</exception>
    public static string Encode(PageCursorPosition position, PageCursorBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!position.IsNonNegative)
        {
            throw new ArgumentException("A cursor position cannot be negative.", nameof(position));
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteNumber("s", (int)position.Shape);
            writer.WriteStartArray("p");
            writer.WriteNumberValue(position.Offset);
            if (position.Shape != PageCursorShape.Offset)
            {
                writer.WriteNumberValue(position.SecondOffset);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("b");
            foreach ((string key, string value) in binding.Parts)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        byte[] payload = buffer.ToArray();
        string cursor = Base64Url.EncodeToString(payload) + Separator + Checksum(payload);
        return cursor.Length <= MaximumLength
            ? cursor
            : throw new ArgumentException("The cursor binding is too large.", nameof(binding));
    }

    /// <summary>
    /// Reads the position a cursor names and checks it against the state of this request. A missing cursor starts at
    /// the first row. A cursor that is malformed, edited, too long, of another version or shape, or for another
    /// request is invalid input at <c>cursor</c>. A cursor whose state part no longer matches is a conflict that
    /// carries <paramref name="current"/>. The position still needs bounding by the caller.
    /// </summary>
    /// <param name="cursor">The cursor the caller passed, or null for the first page.</param>
    /// <param name="binding">The state the result is in now.</param>
    /// <param name="shape">The shape of position this result pages with.</param>
    /// <param name="current">The current state to report when the cursor is stale, or null when there is none.</param>
    public static OperationResult<PageCursorPosition> Decode(
        string? cursor,
        PageCursorBinding binding,
        PageCursorShape shape = PageCursorShape.Offset,
        ConflictState? current = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (cursor is null)
        {
            return new OperationSuccess<PageCursorPosition>(PageCursorPosition.Start(shape));
        }

        if (!TryRead(cursor, out PageCursorPosition position, out Dictionary<string, string> stored) ||
            position.Shape != shape)
        {
            return Invalid();
        }

        return binding.Compare(stored) switch
        {
            null => new OperationSuccess<PageCursorPosition>(position),
            PageCursorMismatch.Stale => Stale(current),
            _ => Invalid(),
        };
    }

    /// <summary>
    /// Reads the position a cursor names without checking its binding, for a reader that must acquire the state the
    /// binding names before it can build that binding. The reader must still call <see cref="Decode"/> with the
    /// binding before it trusts the cursor. A missing cursor starts at the first row, and a cursor that is malformed,
    /// edited, too long, or of another version or shape is invalid input at <c>cursor</c>.
    /// </summary>
    /// <param name="cursor">The cursor the caller passed, or null for the first page.</param>
    /// <param name="shape">The shape of position this result pages with.</param>
    public static OperationResult<PageCursorPosition> Peek(string? cursor, PageCursorShape shape)
    {
        if (cursor is null)
        {
            return new OperationSuccess<PageCursorPosition>(PageCursorPosition.Start(shape));
        }

        return TryRead(cursor, out PageCursorPosition position, out _) && position.Shape == shape
            ? new OperationSuccess<PageCursorPosition>(position)
            : Invalid();
    }

    /// <summary>
    /// Builds the failure for a cursor that cannot be used with this request.
    /// </summary>
    public static OperationInvalidInput Invalid()
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
    public static OperationConflict Stale(ConflictState? current = null)
    {
        return new OperationConflict(
            SharedReasonCodes.CursorStale,
            "The data behind this cursor changed since the first page. Call again without cursor to read the " +
            "current data.",
            current);
    }

    /// <summary>
    /// Reads the payload of a cursor whose checksum holds, and returns false for any cursor that is not exactly what
    /// <see cref="Encode"/> writes.
    /// </summary>
    private static bool TryRead(
        string cursor,
        out PageCursorPosition position,
        out Dictionary<string, string> binding)
    {
        position = default;
        binding = [];
        int separator = cursor.IndexOf(Separator, StringComparison.Ordinal);
        if (cursor.Length > MaximumLength || separator <= 0)
        {
            return false;
        }

        try
        {
            byte[] payload = Base64Url.DecodeFromChars(cursor.AsSpan(0, separator));
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(cursor[(separator + 1)..]),
                Encoding.ASCII.GetBytes(Checksum(payload))))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.GetProperty("v").GetInt32() != Version ||
                !TryReadPosition(root, out position))
            {
                return false;
            }

            foreach (JsonProperty part in root.GetProperty("b").EnumerateObject())
            {
                binding[part.Name] = part.Value.GetString() ?? throw new FormatException("A binding part is null.");
            }

            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the shape and numbers of a position, and returns false when they do not fit each other.
    /// </summary>
    private static bool TryReadPosition(JsonElement root, out PageCursorPosition position)
    {
        position = default;
        JsonElement[] numbers = [.. root.GetProperty("p").EnumerateArray()];
        int first = numbers.Length > 0 ? numbers[0].GetInt32() : -1;
        int second = numbers.Length > 1 ? numbers[1].GetInt32() : 0;
        PageCursorShape? shape = root.GetProperty("s").GetInt32() switch
        {
            (int)PageCursorShape.Offset when numbers.Length == 1 => PageCursorShape.Offset,
            (int)PageCursorShape.TwoOffsets when numbers.Length == 2 => PageCursorShape.TwoOffsets,
            (int)PageCursorShape.ProviderPage when numbers.Length == 2 => PageCursorShape.ProviderPage,
            _ => null,
        };
        if (shape is null || first < 0 || second < 0)
        {
            return false;
        }

        position = shape switch
        {
            PageCursorShape.Offset => PageCursorPosition.AtOffset(first),
            PageCursorShape.TwoOffsets => PageCursorPosition.AtOffsets(first, second),
            _ => PageCursorPosition.AtProviderPage(first, second),
        };
        return true;
    }

    /// <summary>
    /// Computes the short checksum that follows the payload.
    /// </summary>
    private static string Checksum(byte[] payload)
    {
        return Base64Url.EncodeToString(SHA256.HashData(payload).AsSpan(0, ChecksumBytes));
    }
}
