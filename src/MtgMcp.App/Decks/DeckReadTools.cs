using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes deterministic local deck reads in every operation mode.
/// </summary>
internal sealed class DeckReadTools
{
    /// <summary>
    /// Stores the local deck boundary used by every read tool.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Builds deck reads that add installed card data to entries.
    /// </summary>
    private readonly DeckCardViewCoordinator cardViews;

    /// <summary>
    /// Builds validation results, with Commander rule facts when asked.
    /// </summary>
    private readonly DeckRuleCoordinator rules;

    /// <summary>
    /// Creates read tools around one process-local deck store, the coordinator that adds card data to entries, and
    /// the coordinator that works out rule facts.
    /// </summary>
    internal DeckReadTools(SqliteDeckStore store, DeckCardViewCoordinator cardViews, DeckRuleCoordinator rules)
    {
        this.store = store;
        this.cardViews = cardViews;
        this.rules = rules;
    }

    /// <summary>
    /// Gets the allowed item values of the list parameters, published in the tool schema.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ItemChoices { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["include"] = [DeckCardViewCoordinator.IncludeCards, DeckCardViewCoordinator.IncludeTags],
            ["fields"] = ScryfallConciseFields.All,
        };

    /// <summary>
    /// Lists one canonical page of local decks.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_list",
        Title = "List Local Decks",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Local deck store: lists canonically ordered local deck summaries using an opaque stable cursor.")]
    internal Task<OperationResult<DeckPage>> ListAsync(
        [Description("Opaque cursor returned by a prior local_deck_list call.")] string? cursor = null,
        [Description("Number of summaries to return, from 1 through 100.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return store.ListAsync(cursor, pageSize, cancellationToken);
    }

    /// <summary>
    /// Gets one complete canonical local deck by stable ID.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_get",
        Title = "Get Local Deck",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: gets one local deck with its stored state: entries, zones, categories, assignments, and " +
        "provider-neutral bindings, plus the entry IDs that edits need. Add include [cards, tags] to also get, " +
        "for each entry, its concise Scryfall card fields (exact printing) and oracle tag slugs from installed " +
        "card data, in pages of entries. Each entry then carries cardData: found, not-installed (run " +
        "scryfall_card_data_sync), or unresolved (no exact Scryfall identity, or not in the installed data). " +
        "A deck too large for one result is also returned in pages: truncated is true with a narrow hint, and " +
        "nextCursor reads the rest.")]
    internal Task<OperationResult<DeckDocumentView>> GetAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description(
            "Optional extras for each entry: cards (concise card fields) and tags (oracle tag slugs). " +
            "When both are given the card leaves out tags unless fields names them. " +
            "Omit to get the stored deck only.")]
        IReadOnlyList<string>? include = null,
        [Description(
            "Only with include cards: concise card fields to return instead of the default set; " +
            "same names as scryfall_search fields.")]
        IReadOnlyList<string>? fields = null,
        [Description(
            "Opaque cursor from nextCursor of a previous local_deck_get call with the same deckId, include, and " +
            "fields.")]
        string? cursor = null,
        [Description(
            "Entries per page, from 1 through 100. With include the default is 25; without include the deck " +
            "comes back whole unless it is too large for one result.")]
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        return cardViews.GetAsync(deckId, include, fields, cursor, pageSize, cancellationToken);
    }

    /// <summary>
    /// Reports local structural invariants for one deck and, on request, Commander rule facts.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_validate",
        Title = "Validate Local Deck",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: checks local references, quantities, zones, and primary categories. With rules " +
        "commander it also reports Commander rule facts from installed Scryfall card data: deck size, commanders " +
        "and whether each is eligible, color identity and entries outside it, singleton breaks, Commander " +
        "legality of every entry, a Game Changer count and list, the partner pairing of two commanders, and the " +
        "checks of a companion in a companion zone. Each fact has a passed flag or a status field and names the " +
        "entries involved. It never names a bracket, a power level, or an overall verdict." +
        UntrustedTextNotes.CardListEntries + " Example: " +
        "{\"deckId\":\"0193e97a-ef95-7700-8000-000000000001\",\"rules\":\"commander\"}.")]
    internal Task<OperationResult<DeckValidationView>> ValidateAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Optional rule set to add facts for: commander. Omit for structural checks only."),
         AllowedValues("commander")]
        string? rules = null,
        CancellationToken cancellationToken = default)
    {
        return this.rules.ValidateAsync(deckId, rules, cancellationToken);
    }

    /// <summary>
    /// Lists opaque local deck backups and the current guarded database fingerprint.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_backup_list",
        Title = "List Local Deck Backups",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Local deck store: lists opaque deck backup metadata without exposing local filesystem paths.")]
    internal Task<OperationResult<DeckBackupPage>> ListBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        return store.Backups.ListAsync(cancellationToken);
    }
}
