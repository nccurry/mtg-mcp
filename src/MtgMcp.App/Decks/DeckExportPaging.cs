using System.Text.Json;
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
/// Carries one chunk of one artifact's content, counted in characters from the start of the content.
/// </summary>
internal sealed record DeckExportChunk(string FileName, int Offset, string Content)
{
    /// <summary>
    /// Gets true when the content continues past this chunk.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets the <c>offset</c> that returns the next chunk, or null on the last chunk.
    /// </summary>
    public int? NextOffset { get; init; }

    /// <summary>
    /// Gets what to pass to read the rest of the content.
    /// </summary>
    public string? Narrow { get; init; }
}

/// <summary>
/// Returns an export bundle's metadata and one chunk of one artifact. The field-preservation report comes with the
/// chunk at offset 0 only.
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
    /// Sets the most JSON-escaped characters one content chunk may take.
    /// </summary>
    internal const int MaximumChunkCharacters = 30_000;

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
        if (offset < 0 || (offset > 0 && offset >= content.Length))
        {
            return new OperationInvalidInput(
                "invalid-offset",
                $"offset must be from 0 to {Math.Max(0, content.Length - 1)} for {chosen.FileName}, which has " +
                $"{content.Length} characters.",
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
    /// Finds how many characters from <paramref name="offset"/> fit the chunk limit once JSON-escaped, without
    /// splitting a surrogate pair.
    /// </summary>
    private static int FitLength(string content, int offset)
    {
        int length = Math.Min(MaximumChunkCharacters, content.Length - offset);
        while (length > 1 && JsonEncodedText.Encode(content.AsSpan(offset, length)).Value.Length > MaximumChunkCharacters)
        {
            length = Math.Max(1, length * 9 / 10);
        }

        if (offset + length < content.Length && length > 1 && char.IsHighSurrogate(content[offset + length - 1]))
        {
            length--;
        }

        return length;
    }
}
