using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Exposes Scryfall facts and evidence without provider or category judgments.
/// </summary>
internal sealed class ScryfallReadTools
{
    /// <summary>
    /// Gets the allowed item values of the list parameters, published in each tool schema.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ItemChoices { get; } =
        new Dictionary<string, IReadOnlyList<string>> { ["fields"] = ScryfallConciseFields.All };

    /// <summary>
    /// Describes the responseFormat parameter of every card-returning tool.
    /// </summary>
    private const string ResponseFormatDescription =
        "Card shape: concise (compact fields for comparing many cards), standard (every card field this server " +
        "returns; the default), or full (standard plus raw provider objects; pages are limited to 25 rows).";

    /// <summary>
    /// Describes the fields parameter of every card-returning tool.
    /// </summary>
    private const string FieldsDescription =
        "Only with responseFormat concise: the fields to return instead of the default set. scryfallId, oracleId, " +
        "and name are always included. Names: scryfallId, oracleId, name, manaCost, manaValue, typeLine, colors, " +
        "colorIdentity, producedMana, keywords, oracleText, power, toughness, loyalty, legalities (formats the " +
        "card is legal in), prices.usd, cheapestPriceUsd, rarity, set, tags, gameChanger, edhrecRank. Extra names " +
        "not in the default set: prices.usdFoil, prices.usdEtched, prices.eur, prices.eurFoil, prices.tix, " +
        "legalitiesAll (every format with its status), games, reserved, layout.";

    /// <summary>
    /// Stores the unified provider and local evidence boundary.
    /// </summary>
    private readonly ScryfallService service;

    /// <summary>
    /// Creates the complete read surface around one Scryfall service.
    /// </summary>
    internal ScryfallReadTools(ScryfallService service)
    {
        this.service = service;
    }

    /// <summary>
    /// Runs one authoritative query or replays its exact request snapshot.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_search",
        Title = "Search Scryfall",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: runs an official Scryfall query with complete provider pagination, then returns a bounded page " +
        "from its immutable exact-request snapshot. This is the main way to find cards of a kind: Scryfall " +
        "evaluates the whole query, including official oracle tags. Filters combine with spaces: otag:ramp " +
        "(oracle tag), t:creature (type), id<=bg (within color identity), mv<=3 (mana value), " +
        "f:commander (legal in a format), usd<2 (price), and o:\"draw a card\" (rules text). " +
        "Example query: otag:removal t:instant id<=wb mv<=2 f:commander. Use scryfall_tags_search to find " +
        "a tag's exact slug, and scryfall_installed_cards_by_tag only to read tag assignments from installed card " +
        "data. " +
        "Every printing-level filter (not:reprint, is:reprint, date, year, set, usd) must match one printing. " +
        "A date or year filter matches reprints printed in that period, so add not:reprint to keep only first " +
        "printings, as in not:reprint year=2024. With unique cards each card appears once, as a printing that " +
        "meets every condition; unique prints lists every matching printing. " +
        "Use responseFormat concise for broad queries: it returns only the fields an agent needs to compare cards. " +
        "Every format is cut to the result size limit, with truncated, narrow, and nextCursor for the rest, so a " +
        "standard or full page can hold fewer cards than pageSize; use concise or fields to fit more.")]
    internal Task<OperationResult<ScryfallSearchResponse>> SearchAsync(
        [Description("Official Scryfall search expression evaluated by Scryfall, not by mtg-mcp.")] string query,
        [Description("Scryfall uniqueness mode: cards, art, or prints."), AllowedValues("cards", "art", "prints")]
        string unique = "cards",
        [Description("Official Scryfall result order name."),
         AllowedValues(
             "name", "set", "released", "rarity", "color", "usd", "tix", "eur", "cmc", "power",
             "toughness", "edhrec", "penny", "artist", "review")]
        string order = "name",
        [Description("Direction: auto, asc, or desc."), AllowedValues("auto", "asc", "desc")]
        string direction = "auto",
        [Description("Include supplemental objects such as tokens and emblems.")] bool includeExtras = false,
        [Description("Include cards in every available language rather than only English.")] bool includeMultilingual = false,
        [Description("Include variation printings in the provider result.")] bool includeVariations = false,
        [Description("Cache policy: default, cache-only, or refresh."), FreshnessPolicyValues]
        string freshnessPolicy = "default",
        [Description(ResponseFormatDescription), ResponseFormatValues] string responseFormat = "standard",
        [Description(FieldsDescription)] IReadOnlyList<string>? fields = null,
        [Description("Opaque cursor for this immutable result.")] string? cursor = null,
        [Description("Items to return, from 1 through 100, or through 25 when responseFormat is full.")]
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.SearchAsync(
            query, unique, order, direction, includeExtras, includeMultilingual,
            includeVariations, freshnessPolicy, cursor, pageSize, responseFormat, fields, cancellationToken));
    }

    /// <summary>
    /// Resolves one card or up to 150 card identities, one row each, in caller order, with stable paging.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_cards_get",
        Title = "Get Scryfall Cards",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: gets one card or an ordered list of up to 150 cards by Scryfall ID, Oracle ID, exact name, " +
        "or set and collector number, from installed card data first and then the official collection " +
        "endpoint, with a row and status for every lookup (found or a miss) and provider batching bounded to " +
        "75 identifiers. A single lookup of kind fuzzy-name returns one row; fuzzy names cannot be mixed with " +
        "other lookups. Pass one lookup to read one card. Example: " +
        "{\"lookups\":[{\"kind\":\"exact-name\",\"value\":\"Sol Ring\"}],\"responseFormat\":\"concise\"}. " +
        "Every response format is cut to the result size limit, with truncated, narrow, and nextCursor for the " +
        "rest, so a standard or full page can hold fewer cards than pageSize; use concise or fields to fit " +
        "more.")]
    internal Task<OperationResult<ScryfallCollectionResponse>> GetCardsAsync(
        [Description(
            "One through 150 card lookups, each {kind, value} (or setCode and collectorNumber for a printing); " +
            "quantities normally require only one lookup row.")]
        IReadOnlyList<ScryfallCardLookup> lookups,
        [Description(
            "Cache policy for the first page: default, cache-only, or refresh; " +
            "a cursor always replays bound evidence."),
         FreshnessPolicyValues]
        string freshnessPolicy = "default",
        [Description("Opaque continuation cursor bound to the complete ordered request and exact evidence.")]
        string? cursor = null,
        [Description("Rows to return, from 1 through 100, or through 25 when responseFormat is full.")]
        int pageSize = 25,
        [Description(ResponseFormatDescription), ResponseFormatValues] string responseFormat = "standard",
        [Description(FieldsDescription)] IReadOnlyList<string>? fields = null,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.GetCollectionAsync(
            lookups, freshnessPolicy, cursor, pageSize, responseFormat, fields, cancellationToken));
    }

    /// <summary>
    /// Returns official autocomplete suggestions.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_autocomplete",
        Title = "Autocomplete Card Name",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: returns ordered official Scryfall autocomplete suggestions from an immutable request snapshot.")]
    internal Task<OperationResult<ScryfallAutocompleteResult>> AutocompleteAsync(
        [Description("Card-name prefix sent to Scryfall autocomplete.")] string query,
        [Description("Include supplemental objects such as tokens and emblems.")] bool includeExtras = false,
        [Description("Cache policy: default, cache-only, or refresh."), FreshnessPolicyValues]
        string freshnessPolicy = "default",
        [Description("Opaque cursor for this immutable result.")] string? cursor = null,
        [Description("Suggestions to return, from 1 through 100.")] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.AutocompleteAsync(
            query, includeExtras, freshnessPolicy, cursor, pageSize, cancellationToken));
    }

    /// <summary>
    /// Reports installed card-data generations without network access.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_data_status",
        Title = "Get Scryfall Card Data Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Installed Scryfall card data: reports active and previous card-data generations, integrity, freshness " +
        "eligibility, and dataset " +
        "counts without network access.")]
    internal Task<OperationResult<ScryfallCorpusStatus>> GetCorpusStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.GetCorpusStatusAsync(cancellationToken));
    }

    /// <summary>
    /// Searches installed community-tag metadata.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_tags_search",
        Title = "Search Scryfall Community Tags",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Installed Scryfall card data: searches installed official community-tag metadata by ID, slug, label, or " +
        "alias without category " +
        "inference. Use it to find a tag's exact slug, then filter cards with scryfall_search and otag:<slug>.")]
    internal Task<OperationResult<ScryfallPage<ScryfallTag>>> SearchTagsAsync(
        [Description("Case-insensitive exact or substring community-tag search text.")] string query,
        [Description("Optional tag kind: oracle or art."), TagKindValues]
        string? tagType = null,
        [Description("Include raw tag objects, which may contain large assignment arrays.")] bool includeRaw = false,
        [Description("Opaque cursor bound to the installed card-data generation.")] string? cursor = null,
        [Description("Tags to return, from 1 through 100.")] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.SearchTagsAsync(query, tagType, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Gets installed cards supported by one community-tag expression.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_installed_cards_by_tag",
        Title = "Get Cards by Scryfall Community Tag",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Installed Scryfall card data: returns cards assigned to one installed tag with direct or explicitly " +
        "requested descendant " +
        "evidence; it does not map tags to deck categories. To filter cards by tag together with type, " +
        "color, mana value, or format, use scryfall_search with otag: instead. Every response format is cut to " +
        "the result size limit, with truncated, narrow, and nextCursor for the rest, so a standard or full page " +
        "can hold fewer cards than pageSize; use concise or fields to fit more.")]
    internal Task<OperationResult<ScryfallCardsByTagResponse>> GetCardsByTagAsync(
        [Description("Exact community tag identity returned by scryfall_tags_search.")] string tagIdentity,
        [Description("Required tag kind: oracle or art."), TagKindValues]
        string tagType,
        [Description("Include assignments inherited from descendant community tags.")] bool includeDescendants = false,
        [Description("Minimum assignment weight accepted by the official tag dataset.")] string minimumWeight = "weak",
        [Description(ResponseFormatDescription), ResponseFormatValues] string responseFormat = "standard",
        [Description(FieldsDescription)] IReadOnlyList<string>? fields = null,
        [Description("Opaque cursor bound to the installed card-data generation and tag expression.")] string? cursor = null,
        [Description("Cards to return, from 1 through 100, or through 25 when responseFormat is full.")]
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.GetCardsByTagAsync(
            tagIdentity,
            tagType,
            includeDescendants,
            minimumWeight,
            cursor,
            pageSize,
            responseFormat,
            fields,
            cancellationToken));
    }
}
