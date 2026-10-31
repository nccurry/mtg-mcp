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
        (List<SpellbookBracketCard> cards, List<SpellbookBracketCombo> combos) = Read(evidence.Data);
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
            cards,
            combos);
    }


    /// <summary>
    /// Checks that a response can be mapped, so a bad response is refused before it is stored in the cache.
    /// </summary>
    internal static void Validate(JsonElement data)
    {
        _ = Read(data);
    }

    /// <summary>
    /// Reads every card and combo of an estimate, and refuses the whole response when one is malformed.
    /// </summary>
    private static (List<SpellbookBracketCard> Cards, List<SpellbookBracketCombo> Combos) Read(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("cards", out JsonElement cards) || cards.ValueKind != JsonValueKind.Array ||
            !data.TryGetProperty("combos", out JsonElement combos) || combos.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("Commander Spellbook returned a bracket estimate without its card and combo lists.");
        }

        return ([.. cards.EnumerateArray().Select(MapCard)], [.. combos.EnumerateArray().Select(MapCombo)]);
    }

    /// <summary>
    /// Maps one classified card. A flag the provider did not state is an error, never false.
    /// </summary>
    private static SpellbookBracketCard MapCard(JsonElement item)
    {
        EnsureObject(item, "a card entry");
        JsonElement card = item.TryGetProperty("card", out JsonElement value) ? value : default;
        return new SpellbookBracketCard(
            Text(card, "name") ?? string.Empty,
            Guid.TryParse(Text(card, "oracleId"), out Guid oracleId) ? oracleId : null,
            OptionalNumber(item, "quantity", 1),
            RequiredFlag(item, "gameChanger"),
            RequiredFlag(item, "massLandDenial"),
            RequiredFlag(item, "extraTurn"),
            RequiredFlag(item, "banned"));
    }

    /// <summary>
    /// Maps one classified combo. A flag or number the provider did not state is an error, never false or zero.
    /// </summary>
    private static SpellbookBracketCombo MapCombo(JsonElement item)
    {
        EnsureObject(item, "a combo entry");
        JsonElement combo = item.TryGetProperty("combo", out JsonElement value) ? value : default;
        List<string> names = [];
        if (combo.ValueKind == JsonValueKind.Object &&
            combo.TryGetProperty("uses", out JsonElement uses) &&
            uses.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement use in uses.EnumerateArray())
            {
                if (use.ValueKind == JsonValueKind.Object &&
                    use.TryGetProperty("card", out JsonElement card) &&
                    Text(card, "name") is { } name)
                {
                    names.Add(name);
                }
            }
        }

        return new SpellbookBracketCombo(
            Text(combo, "id") ?? string.Empty,
            names,
            RequiredFlag(item, "definitelyTwoCard"),
            RequiredFlag(item, "arguablyTwoCard"),
            RequiredNumber(item, "speed"),
            Text(combo, "manaNeeded") ?? string.Empty,
            RequiredNumber(combo, "manaValueNeeded"));
    }

    /// <summary>
    /// Builds the error for a response the server cannot read.
    /// </summary>
    private static SpellbookProviderException Invalid(string message)
    {
        return new SpellbookProviderException(SpellbookFailureKind.Unavailable, "source-invalid-response", message);
    }

    /// <summary>
    /// Refuses a list item that is not an object.
    /// </summary>
    private static void EnsureObject(JsonElement item, string what)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"Commander Spellbook returned {what} that is not an object.");
        }
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
    /// Reads a true or false property; a missing or non-boolean value is an error.
    /// </summary>
    private static bool RequiredFlag(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.ValueKind == JsonValueKind.True;
        }

        throw Invalid($"Commander Spellbook did not state the true or false field {name}.");
    }

    /// <summary>
    /// Reads a whole-number property; a missing or non-integer value is an error.
    /// </summary>
    private static int RequiredNumber(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number))
        {
            return number;
        }

        throw Invalid($"Commander Spellbook did not state the whole-number field {name}.");
    }

    /// <summary>
    /// Reads a whole-number property, or a fallback when the provider leaves it out.
    /// </summary>
    private static int OptionalNumber(JsonElement element, string name, int fallback)
    {
        return element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number)
                ? number
                : fallback;
    }
}
