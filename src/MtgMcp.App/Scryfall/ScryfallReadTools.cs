using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Exposes Scryfall facts and evidence without provider or category judgments.
/// </summary>
internal sealed class ScryfallReadTools
{
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
        "Runs an official Scryfall query with complete provider pagination, then returns a bounded page " +
        "from its immutable exact-request snapshot.")]
    internal Task<OperationResult<ScryfallSearchResult>> SearchAsync(
        [Description("Official Scryfall search expression evaluated by Scryfall, not by mtg-mcp.")] string query,
        [Description("Scryfall uniqueness mode: cards, art, or prints.")] string unique = "cards",
        [Description("Official Scryfall result order name.")] string order = "name",
        [Description("Direction: auto, asc, or desc.")] string direction = "auto",
        bool includeExtras = false,
        bool includeMultilingual = false,
        bool includeVariations = false,
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        [Description("Include each lossless raw provider object; limits pages to 25 when true.")] bool includeRaw = false,
        [Description("Opaque cursor for this immutable result.")] string? cursor = null,
        [Description("Items to return, from 1 through 100, or through 25 when includeRaw is true.")] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.SearchAsync(
            query, unique, order, direction, includeExtras, includeMultilingual,
            includeVariations, freshnessPolicy, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Resolves one exact card identity locally before using Scryfall.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_get",
        Title = "Get Scryfall Card",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Gets one card through an explicit Scryfall ID, Oracle ID, exact/fuzzy name, or printing lookup, " +
        "preferring exact active-corpus cases.")]
    internal Task<OperationResult<ScryfallCardResult>> GetCardAsync(
        [Description(
            "Explicit lookup discriminator and values; fuzzy behavior occurs only when kind is fuzzy-name.")]
        ScryfallCardLookup lookup,
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        [Description("Include the lossless raw provider card and face objects.")] bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.GetCardAsync(lookup, freshnessPolicy, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Resolves and stably pages up to 150 exact card identities while preserving caller order.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_collection",
        Title = "Resolve Scryfall Card Collection",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Resolves and stably pages an ordered card collection from the corpus and official collection " +
        "endpoint with explicit per-position misses and provider batching bounded to 75 identifiers.")]
    internal Task<OperationResult<ScryfallCollectionResult>> GetCollectionAsync(
        [Description("One through 150 exact card lookup descriptors; quantities normally require only one identity row.")]
        IReadOnlyList<ScryfallCardLookup> lookups,
        [Description("Cache policy for the first page: default, cache-only, or refresh; a cursor always replays bound evidence.")]
        string freshnessPolicy = "default",
        [Description("Opaque continuation cursor bound to the complete ordered request and exact evidence.")]
        string? cursor = null,
        [Description("Rows to return, from 1 through 100, or through 25 when includeRaw is true.")]
        int pageSize = 25,
        [Description("Include lossless raw provider card and face objects.")] bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.GetCollectionAsync(lookups, freshnessPolicy, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Lists printings for an Oracle identity.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_prints",
        Title = "List Scryfall Printings",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists deterministically ordered printings for one Oracle ID from the corpus or an official request snapshot.")]
    internal Task<OperationResult<ScryfallPrintsResult>> GetPrintsAsync(
        Guid oracleId,
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        [Description("Include each lossless raw provider card and face object.")] bool includeRaw = false,
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.GetPrintsAsync(oracleId, freshnessPolicy, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Lists rulings for an Oracle identity.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_rulings",
        Title = "List Scryfall Rulings",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists source rulings for one Oracle ID from the corpus; provider refresh additionally requires a Scryfall card ID.")]
    internal Task<OperationResult<ScryfallRulingsResult>> GetRulingsAsync(
        Guid oracleId,
        Guid? scryfallCardId = null,
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        [Description("Include each lossless raw ruling object.")] bool includeRaw = false,
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.GetRulingsAsync(
            oracleId, scryfallCardId, freshnessPolicy, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Lists all sets or gets one exact set.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_sets",
        Title = "Get Scryfall Sets",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Lists official Scryfall sets or resolves one exact set code or ID through an immutable request snapshot.")]
    internal Task<OperationResult<ScryfallSetsResult>> GetSetsAsync(
        string? codeOrId = null,
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        [Description("Include each lossless raw set object.")] bool includeRaw = false,
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.GetSetsAsync(codeOrId, freshnessPolicy, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Reads one named official catalog.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_catalog",
        Title = "Get Scryfall Catalog",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Returns a bounded page from one named official Scryfall catalog snapshot.")]
    internal Task<OperationResult<ScryfallCatalogResult>> GetCatalogAsync(
        string catalog,
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.GetCatalogAsync(catalog, freshnessPolicy, cursor, pageSize, cancellationToken));
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
    [Description("Returns ordered official Scryfall autocomplete suggestions from an immutable request snapshot.")]
    internal Task<OperationResult<ScryfallAutocompleteResult>> AutocompleteAsync(
        string query,
        bool includeExtras = false,
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.AutocompleteAsync(
            query, includeExtras, freshnessPolicy, cursor, pageSize, cancellationToken));
    }

    /// <summary>
    /// Gets metadata for the fixed corpus dataset profile.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_bulk_metadata",
        Title = "Get Scryfall Bulk Metadata",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Returns official metadata for All Cards, Rulings, Oracle Tags, and Art Tags without downloading them.")]
    internal Task<OperationResult<ScryfallBulkMetadataResult>> GetBulkMetadataAsync(
        [Description("Cache policy: default, cache-only, or refresh.")] string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.GetBulkMetadataAsync(freshnessPolicy, cancellationToken));
    }

    /// <summary>
    /// Reports installed corpus generations without network access.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_corpus_status",
        Title = "Get Scryfall Corpus Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Reports active and previous corpus generations, integrity, freshness eligibility, and dataset " +
        "counts without network access.")]
    internal Task<OperationResult<ScryfallCorpusStatus>> GetCorpusStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => service.GetCorpusStatusAsync(cancellationToken));
    }

    /// <summary>
    /// Lists immutable exact-request snapshots.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_snapshot_list",
        Title = "List Scryfall Snapshots",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Lists immutable exact-request snapshot metadata without exposing local paths.")]
    internal Task<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>> ListSnapshotsAsync(
        string? operation = null,
        DateTimeOffset? retrievedAfterUtc = null,
        DateTimeOffset? retrievedBeforeUtc = null,
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.ListSnapshotsAsync(
                operation,
                retrievedAfterUtc,
                retrievedBeforeUtc,
                cursor,
                pageSize,
                cancellationToken));
    }

    /// <summary>
    /// Replays one immutable exact-request snapshot.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_snapshot_get",
        Title = "Get Scryfall Snapshot",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Replays ordered member identities from one immutable Scryfall request snapshot by stable ID, " +
        "optionally including each exact raw object.")]
    internal Task<OperationResult<ScryfallSnapshotPage>> GetSnapshotAsync(
        Guid snapshotId,
        string? cursor = null,
        int pageSize = 25,
        [Description("Include each exact raw snapshot member; compact results retain ordinals and checksums. Limits pages to 25 when true.")] bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.GetSnapshotAsync(snapshotId, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Searches installed community-tag metadata.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_tag_search",
        Title = "Search Scryfall Community Tags",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Searches installed official community-tag metadata by ID, slug, label, or alias without category inference.")]
    internal Task<OperationResult<ScryfallPage<ScryfallTag>>> SearchTagsAsync(
        string query,
        [Description("Optional tag kind: oracle or art.")] string? tagType = null,
        [Description("Include raw tag objects, which may contain large assignment arrays.")] bool includeRaw = false,
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() =>
            service.SearchTagsAsync(query, tagType, cursor, pageSize, includeRaw, cancellationToken));
    }

    /// <summary>
    /// Gets installed cards supported by one community-tag expression.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_cards_by_tag",
        Title = "Get Cards by Scryfall Community Tag",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Returns cards assigned to one installed tag with direct or explicitly requested descendant " +
        "evidence; it does not map tags to deck categories.")]
    internal Task<OperationResult<ScryfallCardsByTagResult>> GetCardsByTagAsync(
        string tagIdentity,
        [Description("Required tag kind: oracle or art.")] string tagType,
        bool includeDescendants = false,
        [Description("Minimum assignment weight accepted by the official tag dataset.")] string minimumWeight = "weak",
        [Description("Include raw tag and card objects; limits pages to 25 when true.")] bool includeRaw = false,
        string? cursor = null,
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
            includeRaw,
            cancellationToken));
    }
}
