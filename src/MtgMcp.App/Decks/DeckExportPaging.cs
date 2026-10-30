using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Decks;

/// <summary>
/// Describes one export artifact without its content: the whole content's length and checksum let a caller check
/// that the chunks it read add up to the file.
/// </summary>
internal sealed record DeckExportArtifactInfo(
    string FileName,
    string MediaType,
    int TotalLength,
    string Sha256,
    string Purpose);

/// <summary>
/// Carries one chunk of one artifact's content. The offset counts characters (UTF-16 code units) from the start of
/// the content, and a chunk never starts or ends inside a surrogate pair.
/// </summary>
internal sealed record DeckExportChunk(string FileName, int Offset, string Content)
{
    /// <summary>
    /// Gets true when the content continues past this chunk.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to pass to read the rest of the content.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>offset</c> that returns the next chunk, or null on the last chunk.
    /// </summary>
    public int? NextOffset { get; init; }
}

/// <summary>
/// Returns an export bundle's metadata and one chunk of one artifact. The field-preservation report comes with the
/// chunk at offset 0 only, and each artifact's total length is in characters.
/// </summary>
internal sealed record DeckExportBundleView(
    int SchemaVersion,
    string FormatId,
    Guid DeckId,
    long DeckRevision,
    DateTimeOffset GeneratedAtUtc,
    string Status,
    IReadOnlyList<DeckExportArtifactInfo> Artifacts,
    DeckExportChunk Chunk,
    IReadOnlyList<DeckFieldPreservation> Preservation);

/// <summary>
/// Cuts an export bundle to the size at which Claude Code still shows a result inline.
/// </summary>
internal static class DeckExportPaging
{
    /// <summary>
    /// Sets the most UTF-8 bytes one content chunk may take once the server writes it as JSON text, with its escapes.
    /// </summary>
    internal const int MaximumChunkBytes = 30_000;

    /// <summary>
    /// Builds one chunk of one artifact of a bundle.
    /// </summary>
    internal static OperationResult<DeckExportBundleView> Page(DeckExportBundle bundle, string? artifact, int offset)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.Artifacts.Count == 0)
        {
            return new OperationInvalidInput("no-artifacts", "The export produced no artifacts.", "formatId");
        }

        DeckExportArtifact? chosen = artifact is null
            ? bundle.Artifacts[0]
            : bundle.Artifacts.FirstOrDefault(value => string.Equals(value.FileName, artifact, StringComparison.Ordinal));
        if (chosen is null)
        {
            return new OperationInvalidInput(
                "unknown-artifact",
                "artifact must be one of: " + string.Join(", ", bundle.Artifacts.Select(value => value.FileName)) + ".",
                "artifact");
        }

        string content = chosen.Content;
        OperationInvalidInput? invalid = ResultPage.CheckOffset("offset", offset, content.Length, "characters");
        if (invalid is not null)
        {
            return invalid;
        }

        if (offset > 0 && offset < content.Length && char.IsLowSurrogate(content[offset]))
        {
            return new OperationInvalidInput(
                "invalid-offset",
                $"offset {offset} is inside a character that takes two code units. Use the nextOffset of the " +
                "previous chunk.",
                "offset");
        }

        int length = FitLength(content, offset);
        int next = offset + length;
        bool more = next < content.Length;
        DeckExportChunk chunk = new(chosen.FileName, offset, content.Substring(offset, length))
        {
            Truncated = more ? true : null,
            NextOffset = more ? next : null,
            Narrow = more
                ? $"Characters {offset} to {next - 1} of {content.Length} of {chosen.FileName} are shown to stay " +
                    $"under the size limit. Call again with artifact \"{chosen.FileName}\" and offset {next} for " +
                    "the rest; the chunks joined in order are the whole file, and sha256 is the checksum of the whole file."
                : null,
        };
        return new OperationSuccess<DeckExportBundleView>(new DeckExportBundleView(
            bundle.SchemaVersion,
            bundle.FormatId,
            bundle.DeckId,
            bundle.DeckRevision,
            bundle.GeneratedAtUtc,
            bundle.Status,
            [.. bundle.Artifacts.Select(value => new DeckExportArtifactInfo(
                value.FileName,
                value.MediaType,
                value.Content.Length,
                value.Sha256,
                value.Purpose))],
            chunk,
            offset == 0 ? bundle.Preservation : []));
    }

    /// <summary>
    /// Finds how many characters from <paramref name="offset"/> fit the chunk limit once the server writes them as
    /// JSON text, never ending a chunk inside a surrogate pair.
    /// </summary>
    private static int FitLength(string content, int offset)
    {
        JavaScriptEncoder encoder = ToolCallPolicy.SerializerOptions.Encoder ?? JavaScriptEncoder.Default;
        int length = Math.Min(MaximumChunkBytes, content.Length - offset);
        while (true)
        {
            length = WholeCharacters(content, offset, length);
            if (length <= 1 || EncodedBytes(content, offset, length, encoder) <= MaximumChunkBytes)
            {
                return length;
            }

            length = Math.Max(1, length * 9 / 10);
        }
    }

    /// <summary>
    /// Adjusts a span so that it does not end between the two halves of a surrogate pair: it gives up the first half
    /// when the span is longer than one code unit, and takes the second half when the span is only the first half.
    /// </summary>
    private static int WholeCharacters(string content, int offset, int length)
    {
        int end = offset + length;
        if (end >= content.Length || !char.IsHighSurrogate(content[end - 1]))
        {
            return length;
        }

        return length > 1 ? length - 1 : 2;
    }

    /// <summary>
    /// Counts the UTF-8 bytes of a span of the content as JSON text, with the escapes the server writes.
    /// </summary>
    private static int EncodedBytes(string content, int offset, int length, JavaScriptEncoder encoder)
    {
        return Encoding.UTF8.GetByteCount(JsonEncodedText.Encode(content.AsSpan(offset, length), encoder).Value);
    }
}
