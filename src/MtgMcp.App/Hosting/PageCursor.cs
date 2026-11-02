using System.Buffers.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Names where the next page of a paged result starts: one position for a single list, or one per list for a result
/// of two lists. A position is a row offset, or the offset or page number a provider uses.
/// </summary>
/// <param name="First">The position in the only list, or in the first of two lists.</param>
/// <param name="Second">The position in the second list, or null for a result of one list.</param>
internal readonly record struct PageCursorPosition(int First, int? Second = null);

/// <summary>
/// Says how a cursor's binding differs from the state it was read against.
/// </summary>
internal enum PageCursorMismatch
{
    /// <summary>
    /// The cursor was issued for another request or subject, or binds other parts than this request does.
    /// </summary>
    OtherRequest,

    /// <summary>
    /// The cursor was issued for this request, but the state it names has changed.
    /// </summary>
    Stale,
}

/// <summary>
/// Names the state a cursor was issued for, so a cursor read against other state is refused. Each part is either
/// identity, which must match exactly or the cursor belongs to another request, or state, which may change between
/// pages and makes the cursor stale.
/// </summary>
internal sealed class PageCursorBinding
{
    /// <summary>
    /// Names the request hash part, an identity part.
    /// </summary>
    private const string RequestKey = "request";

    /// <summary>
    /// Names the deck id part, an identity part.
    /// </summary>
    private const string DeckKey = "deck";

    /// <summary>
    /// Names the artifact id part, an identity part.
    /// </summary>
    private const string ArtifactKey = "artifact";

    /// <summary>
    /// Names the deck revision part, a state part.
    /// </summary>
    private const string DeckRevisionKey = "deckRevision";

    /// <summary>
    /// Names the preview fingerprint part, a state part.
    /// </summary>
    private const string PreviewKey = "preview";

    /// <summary>
    /// Names the remote or tree fingerprint part, a state part.
    /// </summary>
    private const string RemoteKey = "remote";

    /// <summary>
    /// Names the artifact checksum part, a state part.
    /// </summary>
    private const string ArtifactChecksumKey = "artifactChecksum";

    /// <summary>
    /// Names the source checksum part, a state part.
    /// </summary>
    private const string SourceKey = "source";

    /// <summary>
    /// Holds the parts by name, in ordinal order.
    /// </summary>
    private readonly ImmutableSortedDictionary<string, string> parts;

    /// <summary>
    /// Creates a binding from named parts.
    /// </summary>
    private PageCursorBinding(ImmutableSortedDictionary<string, string> parts)
    {
        this.parts = parts;
    }

    /// <summary>
    /// Gets a binding with no parts, for a result that nothing can change under.
    /// </summary>
    internal static PageCursorBinding None { get; } =
        new(ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Gets the parts, in ordinal order of their names.
    /// </summary>
    internal IReadOnlyDictionary<string, string> Parts => parts;

    /// <summary>
    /// Binds to the hash of the request arguments that must not change between pages.
    /// </summary>
    internal static PageCursorBinding ForRequest(string requestHash)
    {
        return None.With(RequestKey, requestHash);
    }

    /// <summary>
    /// Binds to one local deck and its revision.
    /// </summary>
    internal static PageCursorBinding ForDeck(Guid deckId, long revision)
    {
        return None.With(DeckKey, deckId.ToString("N"))
            .With(DeckRevisionKey, revision.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Binds to the fingerprint of the preview being paged.
    /// </summary>
    internal static PageCursorBinding ForPreview(string previewFingerprint)
    {
        return None.With(PreviewKey, previewFingerprint);
    }

    /// <summary>
    /// Binds to the fingerprint of the remote deck or folder tree being paged.
    /// </summary>
    internal static PageCursorBinding ForRemote(string remoteFingerprint)
    {
        return None.With(RemoteKey, remoteFingerprint);
    }

    /// <summary>
    /// Binds to one artifact and the checksum of its content.
    /// </summary>
    internal static PageCursorBinding ForArtifact(string artifactId, string checksum)
    {
        return None.With(ArtifactKey, artifactId).With(ArtifactChecksumKey, checksum);
    }

    /// <summary>
    /// Binds to the checksum of the source data being paged.
    /// </summary>
    internal static PageCursorBinding ForSource(string sourceChecksum)
    {
        return None.With(SourceKey, sourceChecksum);
    }

    /// <summary>
    /// Returns a binding that holds the parts of this binding and of <paramref name="other"/>. A part named by both
    /// takes the value from <paramref name="other"/>.
    /// </summary>
    internal PageCursorBinding And(PageCursorBinding other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new PageCursorBinding(parts.SetItems(other.parts));
    }

    /// <summary>
    /// Compares the parts read from a cursor with this binding: null when they match, and otherwise whether the
    /// cursor belongs to another request or the state it names changed.
    /// </summary>
    internal PageCursorMismatch? Compare(IReadOnlyDictionary<string, string> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (stored.Count != parts.Count)
        {
            return PageCursorMismatch.OtherRequest;
        }

        bool stale = false;
        foreach ((string key, string value) in parts)
        {
            if (!stored.TryGetValue(key, out string? issued))
            {
                return PageCursorMismatch.OtherRequest;
            }

            if (FixedTimeEquals(issued, value))
            {
                continue;
            }

            if (key is RequestKey or DeckKey or ArtifactKey)
            {
                return PageCursorMismatch.OtherRequest;
            }

            stale = true;
        }

        return stale ? PageCursorMismatch.Stale : null;
    }

    /// <summary>
    /// Returns a binding with one more part.
    /// </summary>
    private PageCursorBinding With(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return new PageCursorBinding(parts.SetItem(key, value));
    }

    /// <summary>
    /// Compares two texts without leaking where they first differ.
    /// </summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }
}

/// <summary>
/// Writes and reads the opaque cursor that every paged tool takes and returns. A cursor holds a version, one or two
/// positions, and a binding, with a checksum of all three, so a cursor that was edited or cut is refused instead of
/// starting a page at an invented position.
/// </summary>
internal static class PageCursor
{
    /// <summary>
    /// Limits the length of a cursor in characters, for what is written and for what is read.
    /// </summary>
    internal const int MaximumLength = 1024;

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
    /// Hashes the request arguments that must stay the same from page to page, for
    /// <see cref="PageCursorBinding.ForRequest"/>. Each part is serialized with the server's own options, in the
    /// order given.
    /// </summary>
    internal static string RequestHash(params (object? Value, Type Type)[] parts)
    {
        StringBuilder text = new();
        foreach ((object? value, Type type) in parts)
        {
            text.Append(JsonSerializer.Serialize(value, type, ToolCallPolicy.SerializerOptions)).Append('\u001f');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..32];
    }

    /// <summary>
    /// Writes the cursor for the page that starts at a position, bound to the state the page was read from.
    /// </summary>
    /// <exception cref="ArgumentException">A position is negative or the cursor would pass the length limit.</exception>
    internal static string Encode(PageCursorPosition position, PageCursorBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (position.First < 0 || position.Second < 0)
        {
            throw new ArgumentException("A cursor position cannot be negative.", nameof(position));
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteStartArray("p");
            writer.WriteNumberValue(position.First);
            if (position.Second is int second)
            {
                writer.WriteNumberValue(second);
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
    /// the first row of each list. A cursor that is malformed, edited, too long, of another version, for another
    /// request, or with a position count that does not fit the result is invalid input at <c>cursor</c>. A cursor
    /// whose state part no longer matches is a conflict that carries <paramref name="current"/>.
    /// </summary>
    /// <param name="cursor">The cursor the caller passed, or null for the first page.</param>
    /// <param name="binding">The state the result is in now.</param>
    /// <param name="twoLists">True when the result pages two lists, so the cursor must hold two positions.</param>
    /// <param name="current">The current state to report when the cursor is stale, or null when there is none.</param>
    internal static OperationResult<PageCursorPosition> Decode(
        string? cursor,
        PageCursorBinding binding,
        bool twoLists = false,
        ConflictState? current = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (cursor is null)
        {
            return new OperationSuccess<PageCursorPosition>(new PageCursorPosition(0, twoLists ? 0 : null));
        }

        if (!TryRead(cursor, out PageCursorPosition position, out Dictionary<string, string> stored) ||
            position.Second.HasValue != twoLists)
        {
            return Invalid();
        }

        return binding.Compare(stored) switch
        {
            null => new OperationSuccess<PageCursorPosition>(position),
            PageCursorMismatch.Stale => new OperationConflict(
                SharedReasonCodes.CursorStale,
                "The data behind this cursor changed since the first page. Start again without a cursor.",
                current),
            _ => Invalid(),
        };
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
            if (root.GetProperty("v").GetInt32() != Version)
            {
                return false;
            }

            JsonElement[] positions = [.. root.GetProperty("p").EnumerateArray()];
            if (positions.Length is < 1 or > 2)
            {
                return false;
            }

            int first = positions[0].GetInt32();
            int? second = positions.Length == 2 ? positions[1].GetInt32() : null;
            if (first < 0 || second < 0)
            {
                return false;
            }

            foreach (JsonProperty part in root.GetProperty("b").EnumerateObject())
            {
                binding[part.Name] = part.Value.GetString() ?? throw new FormatException("A binding part is null.");
            }

            position = new PageCursorPosition(first, second);
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the short checksum that follows the payload.
    /// </summary>
    private static string Checksum(byte[] payload)
    {
        return Base64Url.EncodeToString(SHA256.HashData(payload).AsSpan(0, ChecksumBytes));
    }
}
