using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.CardLists;

/// <summary>
/// Holds the example requests published in the descriptions of the card list tools. Tests run each one, so the
/// examples cannot drift from the tools (SCHEMA-006).
/// </summary>
internal static class CardListToolExamples
{
    /// <summary>
    /// Stands for a saved search ID in the examples.
    /// </summary>
    internal const string PlaceholderSnapshotId = "0193e97a-ef95-7700-8000-0000000000a1";

    /// <summary>
    /// Stands for a card ID in the examples.
    /// </summary>
    internal const string PlaceholderCardId = "0193e97a-ef95-7700-8000-0000000000b1";

    /// <summary>
    /// Compares a saved search with a deck: the search results that are not in the deck.
    /// </summary>
    internal const string CompareMinimal =
        "{\"left\":{\"kind\":\"scryfall-snapshot\",\"snapshotId\":\"" + PlaceholderSnapshotId + "\"}," +
        "\"right\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"}," +
        "\"operation\":\"difference\"}";

    /// <summary>
    /// Chains two comparisons: (search union card list) minus a deck.
    /// </summary>
    internal const string CompareNested =
        "{\"left\":{\"kind\":\"compare\"," +
        "\"left\":{\"kind\":\"scryfall-snapshot\",\"snapshotId\":\"" + PlaceholderSnapshotId + "\"}," +
        "\"right\":{\"kind\":\"cards\",\"ids\":[\"" + PlaceholderCardId + "\"]}," +
        "\"operation\":\"union\"}," +
        "\"right\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"}," +
        "\"operation\":\"difference\"}";

    /// <summary>
    /// Uses every option of the comparison.
    /// </summary>
    internal const string CompareFull =
        "{\"left\":{\"kind\":\"scryfall-snapshot\",\"snapshotId\":\"" + PlaceholderSnapshotId + "\"}," +
        "\"right\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId +
        "\",\"expectedRevision\":" + DeckToolExamples.PlaceholderRevision + ",\"zones\":[\"main\",\"commander\"]}," +
        "\"operation\":\"difference\",\"matchBy\":\"oracle\",\"responseFormat\":\"concise\"," +
        "\"fields\":[\"manaValue\",\"tags\"],\"pageSize\":50}";

    /// <summary>
    /// Counts the cards of a deck by mana value.
    /// </summary>
    internal const string QueryMinimal =
        "{\"source\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"}," +
        "\"groupBy\":[\"manaValue\"],\"metrics\":[\"quantity\"]}";

    /// <summary>
    /// Counts colored pips and color sources of a deck.
    /// </summary>
    internal const string QueryPips =
        "{\"source\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"," +
        "\"zones\":[\"main\"]},\"metrics\":[\"pips\",\"sources\"]}";

    /// <summary>
    /// Filters, groups, measures, and returns sorted rows.
    /// </summary>
    internal const string QueryFull =
        "{\"source\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"," +
        "\"expectedRevision\":" + DeckToolExamples.PlaceholderRevision + ",\"zones\":[\"main\"]}," +
        "\"where\":[{\"field\":\"typeLine\",\"op\":\"contains\",\"value\":\"Creature\"}," +
        "{\"field\":\"manaValue\",\"op\":\"lte\",\"value\":3}]," +
        "\"groupBy\":[\"manaValue\"]," +
        "\"metrics\":[\"quantity\",\"cards\",\"sum:priceUsd\",\"pips\"]," +
        "\"rows\":{\"fields\":[\"name\",\"manaValue\",\"priceUsd\"]," +
        "\"sortBy\":[{\"field\":\"priceUsd\",\"direction\":\"desc\"}],\"pageSize\":25}}";
}

/// <summary>
/// Exposes the two card list tools: combine two card lists, and filter, group, and measure one.
/// </summary>
internal sealed class CardListTools
{
    /// <summary>
    /// Describes the sources that both tools accept.
    /// </summary>
    private const string SourceNote =
        "A list source is one of: scryfall-snapshot (a saved scryfall_search), cards (up to 150 Scryfall or Oracle " +
        "IDs), deck (a local deck, optional expectedRevision and zones), deck-backup (a deck as it was in a local " +
        "backup), archidekt-deck (read live), archidekt-snapshot, or compare (a nested comparison, at most 3 " +
        "levels). Deck, backup, and Archidekt sources need their toolsets on; installed card data resolves their " +
        "cards, and entries without a Scryfall card are listed in unmatched, never guessed.";

    /// <summary>
    /// Runs the list operations.
    /// </summary>
    private readonly CardListCoordinator coordinator;

    /// <summary>
    /// Creates the tools around one coordinator.
    /// </summary>
    internal CardListTools(CardListCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Gets the allowed item values of the list parameters, published in the tool schemas.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ItemChoices { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["fields"] = ScryfallConciseFields.All,
            ["groupBy"] = [.. ScryfallListQuery.FieldNames.Where(name => name != "oracleText")],
        };

    /// <summary>
    /// Applies one exact set operation to two card lists.
    /// </summary>
    [McpServerTool(
        Name = "card_lists_compare",
        Title = "Compare Card Lists",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: applies one exact set operation (union, intersection, difference = left minus right, " +
        "symmetric-difference) to two card lists given by reference, so large lists never pass through the " +
        "conversation. Use it for \"search results that are not in my deck\" or \"cards in both decks\". " +
        "matchBy oracle (default) treats printings of one card as the same card. Results are ordered by name and " +
        "paged; send the same request with cursor to read the next page. " + SourceNote +
        UntrustedTextNotes.CardListEntries +
        DeckToolExamples.ExamplePrefix + "minimal): " + CardListToolExamples.CompareMinimal +
        DeckToolExamples.ExamplePrefix + "nested): " + CardListToolExamples.CompareNested +
        DeckToolExamples.ExamplePrefix + "full): " + CardListToolExamples.CompareFull)]
    internal Task<OperationResult<CardListsCompareResult>> CompareAsync(
        [Description("The left list. For difference, the cards to keep come from here.")]
        CardListSourceInput left,
        [Description("The right list.")]
        CardListSourceInput right,
        [Description("Set operation: union, intersection, difference (left minus right), or symmetric-difference."),
         AllowedValues("union", "intersection", "difference", "symmetric-difference")]
        string operation,
        [Description("What makes two cards the same card: oracle (any printing, default), printing, or name."),
         AllowedValues("oracle", "printing", "name")]
        string matchBy = "oracle",
        [Description(
            "Card format: concise (a few fields, best for large lists), standard (default), or full (adds raw " +
            "provider objects; at most 25 per page)."),
         ResponseFormatValues]
        string responseFormat = "standard",
        [Description(
            "Only with responseFormat concise: the fields to return instead of the default set. scryfallId, " +
            "oracleId, and name are always returned.")]
        IReadOnlyList<string>? fields = null,
        [Description("Opaque cursor from nextCursor of a previous call with the same request.")]
        string? cursor = null,
        [Description("Cards per page, from 1 through 100, or through 25 for full. Use 100 for Archidekt sources.")]
        int pageSize = CardListCoordinator.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return coordinator.CompareAsync(
            left,
            right,
            operation,
            matchBy,
            responseFormat,
            fields,
            cursor,
            pageSize,
            cancellationToken);
    }

    /// <summary>
    /// Filters, groups, counts, sums, and sorts one card list.
    /// </summary>
    [McpServerTool(
        Name = "card_list_query",
        Title = "Query A Card List",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: filters, groups, counts, sums, and sorts one card list using installed card data, in one call. " +
        "Use it for the mana curve, type mix, counts by oracle tag, color pips and sources, deck price, and the most " +
        "expensive cards. where holds filters that must all match (ops eq, ne, lt, lte, gt, gte, in, contains); " +
        "groupBy takes up to two fields, and a card with several values (colors, tags) counts once in each group. " +
        "metrics: quantity (copies), cards (distinct cards), sum:/avg:/min:/max: plus a number field, pips (colored " +
        "mana symbols by color; a hybrid symbol counts once for each of its colors and also under hybrid; a " +
        "Phyrexian symbol counts for its color and also under phyrexian), and sources (cards whose produced mana " +
        "includes each color; whether a land enters tapped is not modeled). Sums are exact decimal strings and " +
        "cards with no " +
        "value are counted in missingValues. rows adds a sorted, paged table of cards. Fields: name, manaValue, " +
        "typeLine, cardTypes, subtypes, supertypes, colors, colorIdentity, keywords, tags, rarity, set, power, " +
        "toughness, loyalty, priceUsd and other price columns, cheapestPriceUsd, edhrecRank, gameChanger, " +
        "producedMana, oracleText (contains only), legalIn, bannedIn, games, reserved, layout, and for deck sources " +
        "zone, category, and quantity. " + SourceNote + UntrustedTextNotes.CardListEntries +
        DeckToolExamples.ExamplePrefix + "curve): " + CardListToolExamples.QueryMinimal +
        DeckToolExamples.ExamplePrefix + "pips): " + CardListToolExamples.QueryPips +
        DeckToolExamples.ExamplePrefix + "full): " + CardListToolExamples.QueryFull)]
    internal Task<OperationResult<CardListQueryResult>> QueryAsync(
        [Description("The list to analyze.")]
        CardListSourceInput source,
        [Description("Filters that must all match. Omit to keep every card.")]
        IReadOnlyList<CardListFilterInput>? where = null,
        [Description("Zero to two field names to group by. Omit for one total.")]
        IReadOnlyList<string>? groupBy = null,
        [Description(
            "Metrics for each group: quantity (copies), cards (distinct cards), sum:FIELD, avg:FIELD, min:FIELD, " +
            "max:FIELD (a number field), " +
            "pips, sources. Default: quantity and cards.")]
        IReadOnlyList<string>? metrics = null,
        [Description("Optional per-card rows with chosen fields, sorting, and paging.")]
        CardListRowsInput? rows = null,
        [Description("Opaque cursor from rows.nextCursor of a previous call with the same request.")]
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return coordinator.QueryAsync(source, where, groupBy, metrics, rows, cursor, cancellationToken);
    }
}
