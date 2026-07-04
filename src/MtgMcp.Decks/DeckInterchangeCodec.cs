using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Serializes native snapshots and deterministic companion artifacts with stable web JSON names.
/// </summary>
internal static class DeckInterchangeCodec
{
    /// <summary>
    /// Identifies the only supported native interchange schema.
    /// </summary>
    internal const string NativeSchema = "mtg-mcp.deck/v1";

    /// <summary>
    /// Provides deterministic readable JSON without accepting comments or trailing commas.
    /// </summary>
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Serializes one complete native snapshot with a trailing newline.
    /// </summary>
    internal static string SerializeNative(DeckInterchangeSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(
            new NativeDeckEnvelope(NativeSchema, snapshot.Deck, snapshot.SyncBaselines),
            Options);
        return json + "\n";
    }

    /// <summary>
    /// Parses one exact native schema envelope or reports a safe caller-input failure.
    /// </summary>
    internal static bool TryParseNative(
        string content,
        out DeckInterchangeSnapshot? snapshot,
        out string failure)
    {
        try
        {
            NativeDeckEnvelope? envelope = JsonSerializer.Deserialize<NativeDeckEnvelope>(content, Options);
            if (envelope is null || envelope.Schema != NativeSchema || envelope.Deck is null)
            {
                snapshot = null;
                failure = "The native document does not use the supported mtg-mcp.deck/v1 schema.";
                return false;
            }

            snapshot = new DeckInterchangeSnapshot(envelope.Deck, envelope.SyncBaselines ?? []);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            snapshot = null;
            failure = "The native document is not valid JSON for the supported schema.";
            return false;
        }
    }

    /// <summary>
    /// Computes a lowercase SHA-256 checksum over exact UTF-8 artifact content.
    /// </summary>
    internal static string Sha256(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    /// <summary>
    /// Defines the exact top-level native JSON shape.
    /// </summary>
    private sealed record NativeDeckEnvelope(
        string Schema,
        DeckDocument Deck,
        IReadOnlyList<DeckSyncBaseline>? SyncBaselines);
}
