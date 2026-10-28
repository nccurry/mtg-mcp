using System.Text.Json;

namespace MtgMcp.Spellbook;

/// <summary>
/// Names where bracket facts came from and how they were read, without carrying the provider JSON.
/// </summary>
public sealed record SpellbookFactsEvidence(
    string Name,
    string Operation,
    string Endpoint,
    string SourceApiVersion,
    string ContractChecksum,
    DateTimeOffset RetrievedAtUtc,
    string CacheStatus,
    string SourceChecksum,
    string SourceUrl,
    IReadOnlyList<string> Limitations);

/// <summary>
/// Carries the factual flags Commander Spellbook gives for one card of a deck.
/// </summary>
public sealed record SpellbookBracketCard(
    string Name,
    Guid? OracleId,
    int Quantity,
    bool GameChanger,
    bool MassLandDenial,
    bool ExtraTurn,
    bool Banned);

/// <summary>
/// Carries the factual details of one combo Commander Spellbook found in a deck.
/// </summary>
public sealed record SpellbookBracketCombo(
    string VariantId,
    IReadOnlyList<string> Cards,
    bool DefinitelyTwoCard,
    bool ArguablyTwoCard,
    int Speed,
    string ManaNeeded,
    int ManaValueNeeded);

/// <summary>
/// Carries the bracket-related facts of a deck: per-card flags and per-combo details. The provider's own bracket
/// label and every other summary label are removed before this record is built.
/// </summary>
public sealed record SpellbookBracketFacts(
    SpellbookFactsEvidence Evidence,
    IReadOnlyList<SpellbookBracketCard> Cards,
    IReadOnlyList<SpellbookBracketCombo> Combos);

/// <summary>
/// Reads a Commander Spellbook bracket estimate into facts. It keeps only the fields named in
/// <see cref="SpellbookBracketFacts"/>, so no label can reach a result.
/// </summary>
internal static class SpellbookBracketMapper
{
    /// <summary>
    /// Maps one estimate response. A response that lacks the card or combo list is not a valid estimate.
    /// </summary>
    internal static SpellbookBracketFacts Map(SpellbookEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        JsonElement data = evidence.Data;
        if (!data.TryGetProperty("cards", out JsonElement cards) || cards.ValueKind != JsonValueKind.Array ||
            !data.TryGetProperty("combos", out JsonElement combos) || combos.ValueKind != JsonValueKind.Array)
        {
            throw new SpellbookProviderException(
                SpellbookFailureKind.Unavailable,
                "source-invalid-response",
                "Commander Spellbook returned a bracket estimate without its card and combo lists.");
        }

        return new SpellbookBracketFacts(
            new SpellbookFactsEvidence(
                evidence.Name,
                evidence.Operation,
                evidence.Endpoint,
                evidence.SourceApiVersion,
                evidence.ContractChecksum,
                evidence.RetrievedAtUtc,
                evidence.CacheStatus,
                evidence.SourceChecksum,
                evidence.SourceUrl,
                evidence.Limitations),
            [.. cards.EnumerateArray().Select(MapCard)],
            [.. combos.EnumerateArray().Select(MapCombo)]);
    }

    /// <summary>
    /// Maps one classified card.
    /// </summary>
    private static SpellbookBracketCard MapCard(JsonElement item)
    {
        JsonElement card = item.TryGetProperty("card", out JsonElement value) ? value : default;
        return new SpellbookBracketCard(
            Text(card, "name") ?? string.Empty,
            Guid.TryParse(Text(card, "oracleId"), out Guid oracleId) ? oracleId : null,
            Number(item, "quantity", 1),
            Flag(item, "gameChanger"),
            Flag(item, "massLandDenial"),
            Flag(item, "extraTurn"),
            Flag(item, "banned"));
    }

    /// <summary>
    /// Maps one classified combo.
    /// </summary>
    private static SpellbookBracketCombo MapCombo(JsonElement item)
    {
        JsonElement combo = item.TryGetProperty("combo", out JsonElement value) ? value : default;
        List<string> names = [];
        if (combo.ValueKind == JsonValueKind.Object &&
            combo.TryGetProperty("uses", out JsonElement uses) &&
            uses.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement use in uses.EnumerateArray())
            {
                if (use.TryGetProperty("card", out JsonElement card) && Text(card, "name") is { } name)
                {
                    names.Add(name);
                }
            }
        }

        return new SpellbookBracketCombo(
            Text(combo, "id") ?? string.Empty,
            names,
            Flag(item, "definitelyTwoCard"),
            Flag(item, "arguablyTwoCard"),
            Number(item, "speed", 0),
            Text(combo, "manaNeeded") ?? string.Empty,
            Number(combo, "manaValueNeeded", 0));
    }

    /// <summary>
    /// Reads a text property, or null when it is missing or not text.
    /// </summary>
    private static string? Text(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Reads a true or false property; a missing or non-boolean value is false.
    /// </summary>
    private static bool Flag(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Reads a whole-number property, or a fallback when it is missing or not a whole number.
    /// </summary>
    private static int Number(JsonElement element, string name, int fallback)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number)
                ? number
                : fallback;
    }
}
