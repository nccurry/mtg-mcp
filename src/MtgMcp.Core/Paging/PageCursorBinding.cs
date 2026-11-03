using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MtgMcp.Core.Paging;

/// <summary>
/// Names the state a cursor was issued for, so a cursor read against other state is refused. Each part is either
/// identity, which must match exactly or the cursor belongs to another request, or state, which may change between
/// pages and makes the cursor stale.
/// </summary>
public sealed class PageCursorBinding
{
    /// <summary>
    /// Names the request hash part, an identity part.
    /// </summary>
    private const string RequestKey = "request";

    /// <summary>
    /// Names the scope part, an identity part.
    /// </summary>
    private const string ScopeKey = "scope";

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
    public static PageCursorBinding None { get; } =
        new(ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Gets the parts, in ordinal order of their names.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parts => parts;

    /// <summary>
    /// Binds to the hash of the request arguments that must not change between pages.
    /// </summary>
    public static PageCursorBinding ForRequest(string requestHash)
    {
        return None.With(RequestKey, requestHash);
    }

    /// <summary>
    /// Binds to one local deck and its revision.
    /// </summary>
    public static PageCursorBinding ForDeck(Guid deckId, long revision)
    {
        return None.With(DeckKey, deckId.ToString("N"))
            .With(DeckRevisionKey, revision.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Binds to the fingerprint of the preview being paged.
    /// </summary>
    public static PageCursorBinding ForPreview(string previewFingerprint)
    {
        return None.With(PreviewKey, previewFingerprint);
    }

    /// <summary>
    /// Binds to the fingerprint of the remote deck or folder tree being paged.
    /// </summary>
    public static PageCursorBinding ForRemote(string remoteFingerprint)
    {
        return None.With(RemoteKey, remoteFingerprint);
    }

    /// <summary>
    /// Binds to one artifact and the checksum of its content.
    /// </summary>
    public static PageCursorBinding ForArtifact(string artifactId, string checksum)
    {
        return None.With(ArtifactKey, artifactId).With(ArtifactChecksumKey, checksum);
    }

    /// <summary>
    /// Binds to a named scope, an identity part, and the checksum of the data inside it, a state part.
    /// </summary>
    public static PageCursorBinding ForScope(string scope, string checksum)
    {
        return None.With(ScopeKey, scope).With(SourceKey, checksum);
    }

    /// <summary>
    /// Binds to the checksum of the source data being paged.
    /// </summary>
    public static PageCursorBinding ForSource(string sourceChecksum)
    {
        return None.With(SourceKey, sourceChecksum);
    }

    /// <summary>
    /// Returns a binding that holds the parts of this binding and of <paramref name="other"/>. A part named by both
    /// takes the value from <paramref name="other"/>.
    /// </summary>
    public PageCursorBinding And(PageCursorBinding other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new PageCursorBinding(parts.SetItems(other.parts));
    }

    /// <summary>
    /// Compares the parts read from a cursor with this binding: null when they match, and otherwise whether the
    /// cursor belongs to another request or the state it names changed.
    /// </summary>
    public PageCursorMismatch? Compare(IReadOnlyDictionary<string, string> stored)
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

            if (key is RequestKey or ScopeKey or DeckKey or ArtifactKey)
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
