using System.Globalization;
using System.Text.Json;

namespace MtgMcp.Spellbook;

/// <summary>
/// Owns the pinned Commander Spellbook contract, validation rules, and source evidence limits.
/// </summary>
internal static class SpellbookContract
{
    /// <summary>
    /// Names the source shown with every successful result.
    /// </summary>
    internal const string SourceName = "Commander Spellbook";

    /// <summary>
    /// Names the official website used to credit the source.
    /// </summary>
    internal const string SourceUrl = "https://commanderspellbook.com";

    /// <summary>
    /// Identifies the reviewed public API version.
    /// </summary>
    internal const string ApiVersion = "6.3.3";

    /// <summary>
    /// Identifies the exact bytes in the checked-in narrow OpenAPI snapshot.
    /// </summary>
    internal const string OpenApiChecksum = "27c72677c19d8f0a401ca91e3541f328a055097887d05f65a8e82ce07bd8d5f4";

    /// <summary>
    /// Caps a caller-supplied source query before one provider request.
    /// </summary>
    internal const int MaximumSourceQueryLength = 512;

    /// <summary>
    /// Caps an exact source variant identifier before path escaping.
    /// </summary>
    internal const int MaximumVariantIdLength = 256;

    /// <summary>
    /// Caps one source card name before a deck request leaves the process.
    /// </summary>
    internal const int MaximumCardNameLength = 256;

    /// <summary>
    /// Caps Commander Spellbook commander request rows.
    /// </summary>
    internal const int MaximumCommanderEntries = 12;

    /// <summary>
    /// Caps Commander Spellbook main-deck request rows.
    /// </summary>
    internal const int MaximumMainEntries = 600;

    /// <summary>
    /// Uses a small page size that avoids source defaults intended for interactive browsing.
    /// </summary>
    internal const int DefaultLimit = 20;

    /// <summary>
    /// Uses the first page unless a bounded offset is supplied.
    /// </summary>
    internal const int DefaultOffset = 0;

    /// <summary>
    /// Groups variant results unless the caller explicitly asks otherwise.
    /// </summary>
    internal const bool DefaultGroupByCombo = true;

    /// <summary>
    /// Caps a variant or deck-result page at the source-supported boundary.
    /// </summary>
    internal const int MaximumLimit = 25;

    /// <summary>
    /// Caps source offsets so one tool call stays bounded.
    /// </summary>
    internal const int MaximumOffset = 1_000;

    /// <summary>
    /// Lists facts that limit interpretation of every source response.
    /// </summary>
    internal static IReadOnlyList<string> Limitations { get; } = Array.AsReadOnly<string>(
    [
        "Commander Spellbook data is source evidence, not a deckbuilding decision.",
        "Source results can change after retrieval.",
        "A sent deck entry is not proof that Commander Spellbook recognized it.",
        "Source pagination links can repeat a source query.",
    ]);

    /// <summary>
    /// Requires a nonblank source query while preserving every valid caller character.
    /// </summary>
    internal static string RequiredSourceQuery(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumSourceQueryLength)
        {
            throw Invalid(
                "invalid-source-query",
                $"{parameterName} must contain 1 through {MaximumSourceQueryLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return value;
    }

    /// <summary>
    /// Accepts an omitted source query or validates one supplied source query without rewriting it.
    /// </summary>
    internal static string? OptionalSourceQuery(string? value, string parameterName)
    {
        return value is null ? null : RequiredSourceQuery(value, parameterName);
    }

    /// <summary>
    /// Requires an exact source variant identifier before it becomes one escaped path segment.
    /// </summary>
    internal static string RequiredVariantId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumVariantIdLength)
        {
            throw Invalid(
                "invalid-variant-id",
                $"{parameterName} must contain 1 through {MaximumVariantIdLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return value;
    }

    /// <summary>
    /// Applies the explicit source page defaults and rejects values outside the supported boundary.
    /// </summary>
    internal static SpellbookPageRequest Page(SpellbookPageOptions? value)
    {
        int limit = value?.Limit ?? DefaultLimit;
        int offset = value?.Offset ?? DefaultOffset;
        bool groupByCombo = value?.GroupByCombo ?? DefaultGroupByCombo;
        if (limit is < 1 or > MaximumLimit || offset is < 0 or > MaximumOffset)
        {
            throw Invalid(
                "invalid-page-options",
                $"limit must be 1 through {MaximumLimit.ToString(CultureInfo.InvariantCulture)} and offset must be 0 through {MaximumOffset.ToString(CultureInfo.InvariantCulture)}.");
        }

        return new SpellbookPageRequest(limit, offset, groupByCombo);
    }

    /// <summary>
    /// Validates, groups, and ordinally sorts both deck zones before source serialization and cache hashing.
    /// </summary>
    internal static SpellbookDeckRequest Deck(SpellbookDeckRequest? value)
    {
        if (value is null)
        {
            throw Invalid("invalid-deck-request", "deck is required.");
        }

        IReadOnlyList<SpellbookDeckEntry> commanders = NormalizeZone(
            value.Commanders,
            "deck.commanders",
            MaximumCommanderEntries);
        IReadOnlyList<SpellbookDeckEntry> main = NormalizeZone(value.Main, "deck.main", MaximumMainEntries);
        if (commanders.Count == 0 && main.Count == 0)
        {
            throw Invalid("empty-deck-request", "deck must contain at least one commander or main-deck entry.");
        }

        return new SpellbookDeckRequest(commanders, main);
    }

    /// <summary>
    /// Parses one complete source object while preserving unknown fields and nullable values.
    /// </summary>
    internal static JsonElement ParseResponseObject(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Unsupported();
            }

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new SpellbookProviderException(
                SpellbookFailureKind.Unsupported,
                "provider-contract-unsupported",
                "Commander Spellbook returned data outside the pinned JSON contract.",
                innerException: exception);
        }
    }

    /// <summary>
    /// Normalizes one source deck zone without trimming or otherwise changing card names.
    /// </summary>
    private static IReadOnlyList<SpellbookDeckEntry> NormalizeZone(
        IReadOnlyList<SpellbookDeckEntry>? values,
        string parameterName,
        int maximumEntries)
    {
        if (values is null)
        {
            throw Invalid("invalid-deck-request", $"{parameterName} is required.");
        }

        SortedDictionary<string, int> quantities = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Count; index++)
        {
            SpellbookDeckEntry? entry = values[index];
            if (entry is null || string.IsNullOrWhiteSpace(entry.Card) || entry.Card.Length > MaximumCardNameLength || entry.Quantity <= 0)
            {
                throw Invalid(
                    "invalid-deck-entry",
                    $"{parameterName}[{index.ToString(CultureInfo.InvariantCulture)}] must have a nonblank card name no longer than {MaximumCardNameLength.ToString(CultureInfo.InvariantCulture)} characters and a positive quantity.");
            }

            try
            {
                quantities.TryGetValue(entry.Card, out int current);
                quantities[entry.Card] = checked(current + entry.Quantity);
            }
            catch (OverflowException exception)
            {
                throw new SpellbookProviderException(
                    SpellbookFailureKind.InvalidInput,
                    "deck-quantity-overflow",
                    "Repeated deck entry quantities exceed the supported integer range.",
                    innerException: exception);
            }
        }

        if (quantities.Count > maximumEntries)
        {
            throw Invalid(
                "too-many-deck-entries",
                $"{parameterName} cannot contain more than {maximumEntries.ToString(CultureInfo.InvariantCulture)} source entries.");
        }

        List<SpellbookDeckEntry> normalized = new(quantities.Count);
        foreach (KeyValuePair<string, int> quantity in quantities)
        {
            normalized.Add(new SpellbookDeckEntry(quantity.Key, quantity.Value));
        }

        return Array.AsReadOnly(normalized.ToArray());
    }

    /// <summary>
    /// Creates a safe invalid-input failure.
    /// </summary>
    private static SpellbookProviderException Invalid(string reasonCode, string message)
    {
        return new SpellbookProviderException(SpellbookFailureKind.InvalidInput, reasonCode, message);
    }

    /// <summary>
    /// Creates one unsupported-provider-contract failure.
    /// </summary>
    private static SpellbookProviderException Unsupported()
    {
        return new SpellbookProviderException(
            SpellbookFailureKind.Unsupported,
            "provider-contract-unsupported",
            "Commander Spellbook returned data outside the pinned JSON contract.");
    }
}
