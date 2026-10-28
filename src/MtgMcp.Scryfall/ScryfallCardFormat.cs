using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Lists the field names a concise card result can carry.
/// </summary>
public static class ScryfallConciseFields
{
    /// <summary>
    /// Gets the fields every concise card carries, even when the caller narrows the field list.
    /// </summary>
    public static IReadOnlyList<string> Always { get; } = ["scryfallId", "oracleId", "name"];

    /// <summary>
    /// Gets the fields a concise card carries when the caller names none, in output order.
    /// </summary>
    public static IReadOnlyList<string> Default { get; } =
    [
        "scryfallId", "oracleId", "name", "manaCost", "manaValue", "typeLine", "colors", "colorIdentity",
        "producedMana", "keywords", "oracleText", "power", "toughness", "loyalty", "legalities", "prices.usd",
        "cheapestPriceUsd", "rarity", "set", "tags", "gameChanger", "edhrecRank",
    ];

    /// <summary>
    /// Gets the fields a caller can add by name; they are not in the default set.
    /// </summary>
    public static IReadOnlyList<string> Extra { get; } =
    [
        "prices.usdFoil", "prices.usdEtched", "prices.eur", "prices.eurFoil", "prices.tix", "legalitiesAll",
        "games", "reserved", "layout",
    ];

    /// <summary>
    /// Gets every field name a caller can ask for: the default set, then the extra fields.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [.. Default, .. Extra];
}

/// <summary>
/// Describes how a card result is shaped: the response format and, for concise, the chosen fields.
/// </summary>
internal sealed record ScryfallCardFormat
{
    /// <summary>
    /// Names the compact card projection.
    /// </summary>
    internal const string Concise = "concise";

    /// <summary>
    /// Names the default format, which keeps every field of the mapped card.
    /// </summary>
    internal const string Standard = "standard";

    /// <summary>
    /// Names the mapped card together with the raw provider objects.
    /// </summary>
    internal const string Full = "full";

    /// <summary>
    /// Gets the standard format, which is the default.
    /// </summary>
    internal static ScryfallCardFormat StandardFormat { get; } = new(Standard, null);

    /// <summary>
    /// Creates a format after its values were checked by <see cref="Parse"/>.
    /// </summary>
    private ScryfallCardFormat(string name, IReadOnlySet<string>? selectedFields)
    {
        Name = name;
        SelectedFields = selectedFields;
    }

    /// <summary>
    /// Gets the response format name.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    /// Gets the concise fields to write, or null for the other formats.
    /// </summary>
    internal IReadOnlySet<string>? SelectedFields { get; }

    /// <summary>
    /// Gets a value showing whether results use the concise projection.
    /// </summary>
    internal bool IsConcise => string.Equals(Name, Concise, StringComparison.Ordinal);

    /// <summary>
    /// Gets a value showing whether results carry the raw provider objects.
    /// </summary>
    internal bool IncludeRaw => string.Equals(Name, Full, StringComparison.Ordinal);

    /// <summary>
    /// Gets a value showing whether any selected field needs installed card data.
    /// </summary>
    internal bool NeedsInstalledData => NeedsTags || NeedsPrice;

    /// <summary>
    /// Gets a value showing whether tag slugs are selected.
    /// </summary>
    internal bool NeedsTags => SelectedFields?.Contains("tags") == true;

    /// <summary>
    /// Gets a value showing whether the cheapest USD price is selected.
    /// </summary>
    internal bool NeedsPrice => SelectedFields?.Contains("cheapestPriceUsd") == true;

    /// <summary>
    /// Checks the response format and field list and builds the format.
    /// </summary>
    internal static OperationResult<ScryfallCardFormat> Parse(string? responseFormat, IReadOnlyList<string>? fields)
    {
        string name = responseFormat ?? Standard;
        if (name is not (Concise or Standard or Full))
        {
            return new OperationInvalidInput(
                "invalid-response-format",
                "responseFormat must be concise, standard, or full.",
                "responseFormat");
        }

        if (fields is not null && !string.Equals(name, Concise, StringComparison.Ordinal))
        {
            return new OperationInvalidInput(
                "fields-require-concise",
                "fields can only be used with responseFormat concise.",
                "fields");
        }

        if (!string.Equals(name, Concise, StringComparison.Ordinal))
        {
            return new OperationSuccess<ScryfallCardFormat>(new ScryfallCardFormat(name, null));
        }

        OperationInvalidInput? unknown = FindUnknownField(fields);
        if (unknown is not null)
        {
            return unknown;
        }

        HashSet<string> selected = new(StringComparer.Ordinal);
        foreach (string field in fields is null ? ScryfallConciseFields.Default : ScryfallConciseFields.Always)
        {
            selected.Add(field);
        }

        foreach (string field in fields ?? [])
        {
            selected.Add(field);
        }

        return new OperationSuccess<ScryfallCardFormat>(new ScryfallCardFormat(name, selected));
    }

    /// <summary>
    /// Finds the first field name that is not offered, and names every offered field in the message.
    /// </summary>
    private static OperationInvalidInput? FindUnknownField(IReadOnlyList<string>? fields)
    {
        if (fields is null)
        {
            return null;
        }

        for (int index = 0; index < fields.Count; index++)
        {
            if (!ScryfallConciseFields.All.Contains(fields[index], StringComparer.Ordinal))
            {
                return new OperationInvalidInput(
                    "invalid-fields",
                    $"Unknown field '{fields[index]}'. Allowed names: {string.Join(", ", ScryfallConciseFields.All)}.",
                    $"fields[{index}]");
            }
        }

        return null;
    }
}
