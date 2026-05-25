using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes MCP tools for workspace.
/// </summary>
[McpServerToolType]
public sealed class WorkspaceTools
{
    /// <summary>
    /// Stores the decks.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Stores the operation mode.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Handles workspace tools.
    /// </summary>
    public WorkspaceTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Creates the local deck.
    /// </summary>
    [McpServerTool(
        Name = "create_local_deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description(
        "Create an offline local deck workspace. "
            + "If local versus Archidekt is ambiguous, ask the user first or use start_deck_workspace."
    )]
    public Task<DeckWorkspace> CreateLocalDeckAsync(
        string name,
        string format = "commander",
        string? description = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("create_local_deck");
        return decks.CreateLocalDeckAsync(name, format, description, cancellationToken);
    }

    /// <summary>
    /// Handles start deck workspace.
    /// </summary>
    [McpServerTool(
        Name = "start_deck_workspace",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description(
        "Preferred first deck workspace tool. "
            + "Requires explicit mode 'local', 'archidekt', or 'moxfield'; if unclear, ask the user before calling. "
            + "Archidekt mode also requires an explicit writeBack choice."
    )]
    public Task<DeckWorkspace> StartDeckWorkspaceAsync(
        string? mode = null,
        string? name = null,
        string format = "commander",
        string? description = null,
        string? archidektDeckIdOrUrl = null,
        string? moxfieldDeckIdOrUrl = null,
        bool? writeBack = null,
        string? decklist = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("start_deck_workspace");
        return decks.StartDeckWorkspaceAsync(
            mode,
            name,
            format,
            description,
            archidektDeckIdOrUrl,
            moxfieldDeckIdOrUrl,
            writeBack,
            decklist,
            cancellationToken
        );
    }

    /// <summary>
    /// Lists the local decks.
    /// </summary>
    [McpServerTool(
        Name = "list_local_decks",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("List saved local-only deck workspaces.")]
    public Task<IReadOnlyList<DeckWorkspace>> ListLocalDecksAsync(
        CancellationToken cancellationToken = default
    )
    {
        return decks.ListLocalWorkspacesAsync(cancellationToken);
    }

    /// <summary>
    /// Opens the local deck.
    /// </summary>
    [McpServerTool(
        Name = "open_local_deck",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Open a saved local or cached Archidekt workspace by workspace id.")]
    public Task<DeckWorkspace> OpenLocalDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        return decks.OpenLocalDeckAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Opens the archidekt deck.
    /// </summary>
    [McpServerTool(
        Name = "open_archidekt_deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description(
        "Open an Archidekt deck by id or URL. "
            + "Requires explicit writeBack true or false; ask the user when writeback intent is unclear. "
            + "Returns a compact workspace summary; use export_deck, analyze_deck, or get_deck_facets for details."
    )]
    public async Task<DeckOpenResult> OpenArchidektDeckAsync(
        string deckIdOrUrl,
        bool? writeBack = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("open_archidekt_deck");
        if (!writeBack.HasValue)
        {
            throw new InvalidOperationException(
                "Archidekt writeback intent is ambiguous. "
                    + "Ask the user whether edits should write back to Archidekt or stay local-only."
            );
        }

        DeckWorkspace workspace = await decks.OpenArchidektDeckAsync(deckIdOrUrl, writeBack.Value, cancellationToken)
            .ConfigureAwait(false);
        return CreateOpenResult(workspace);
    }

    /// <summary>
    /// Imports a Moxfield deck.
    /// </summary>
    [McpServerTool(
        Name = "open_moxfield_deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description(
        "Import a public or unlisted Moxfield deck by id or URL as a generic local workspace. "
            + "Moxfield writeback is not supported; use copy_workspace_to_archidekt to migrate later."
    )]
    public async Task<DeckOpenResult> OpenMoxfieldDeckAsync(
        string deckIdOrUrl,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("open_moxfield_deck");
        DeckWorkspace workspace = await decks.ImportMoxfieldDeckAsync(deckIdOrUrl, cancellationToken)
            .ConfigureAwait(false);
        return CreateOpenResult(workspace);
    }

    /// <summary>
    /// Creates an Archidekt deck.
    /// </summary>
    [McpServerTool(
        Name = "create_archidekt_deck",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description(
        "Create an empty Archidekt deck with writeback enabled. "
            + "Defaults to private visibility unless visibility is explicitly public or unlisted."
    )]
    public async Task<DeckOpenResult> CreateArchidektDeckAsync(
        string name,
        string format = "commander",
        string? description = null,
        string visibility = "private",
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("create_archidekt_deck");
        DeckWorkspace workspace = await decks.CreateArchidektDeckAsync(
                name,
                format,
                description,
                visibility,
                cancellationToken)
            .ConfigureAwait(false);
        return CreateOpenResult(workspace);
    }

    /// <summary>
    /// Copies a workspace into Archidekt.
    /// </summary>
    [McpServerTool(
        Name = "copy_workspace_to_archidekt",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description(
        "Dry-run or apply a full workspace copy into a new or existing Archidekt deck. "
            + "dryRun defaults true; applying to a non-empty destination requires allowNonEmptyDestination=true "
            + "to append or replaceExistingDestination=true to replace its cards."
    )]
    public Task<ArchidektCopyResult> CopyWorkspaceToArchidektAsync(
        string workspaceId,
        bool dryRun = true,
        bool createNew = true,
        string? destinationDeckIdOrUrl = null,
        string? name = null,
        string? format = null,
        string? description = null,
        string visibility = "private",
        bool allowNonEmptyDestination = false,
        bool replaceExistingDestination = false,
        CancellationToken cancellationToken = default
    )
    {
        if (dryRun)
        {
            operationMode.EnsureCanWritePlanningState("copy_workspace_to_archidekt");
        }
        else
        {
            operationMode.EnsureCanMutate("copy_workspace_to_archidekt");
        }

        return decks.CopyWorkspaceToArchidektAsync(
            workspaceId,
            dryRun,
            createNew,
            destinationDeckIdOrUrl,
            name,
            format,
            description,
            visibility,
            allowNonEmptyDestination,
            replaceExistingDestination,
            cancellationToken);
    }

    /// <summary>
    /// Lists the archidekt decks.
    /// </summary>
    [McpServerTool(
        Name = "list_archidekt_decks",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("List decks visible to the configured Archidekt credentials.")]
    public Task<IReadOnlyList<ArchidektDeckSummary>> ListArchidektDecksAsync(
        CancellationToken cancellationToken = default
    )
    {
        return decks.ListArchidektDecksAsync(cancellationToken);
    }

    /// <summary>
    /// Imports the decklist.
    /// </summary>
    [McpServerTool(
        Name = "import_decklist",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Parse and import a decklist into a new local workspace.")]
    public Task<DeckWorkspace> ImportDecklistAsync(
        string decklist,
        string name,
        string format = "commander",
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("import_decklist");
        return decks.ImportDecklistAsync(decklist, name, format, cancellationToken);
    }

    /// <summary>
    /// Exports the deck.
    /// </summary>
    [McpServerTool(
        Name = "export_deck",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Export a deck workspace as a grouped text decklist.")]
    public Task<string> ExportDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        return decks.ExportDeckAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Parses the decklist.
    /// </summary>
    [McpServerTool(
        Name = "parse_decklist",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Parse a decklist without saving it.")]
    public ParsedDecklist ParseDecklist(string decklist)
    {
        return DeckWorkspaceService.ParseDecklist(decklist);
    }

    /// <summary>
    /// Validates the deck.
    /// </summary>
    [McpServerTool(
        Name = "validate_deck",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Validate workspace deck rules with lightweight format checks.")]
    public Task<DeckValidationResult> ValidateDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        return decks.ValidateDeckAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes the deck.
    /// </summary>
    [McpServerTool(
        Name = "analyze_deck",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Analyze category counts, type counts, color identity, curve, and metadata gaps.")]
    public Task<DeckAnalysis> AnalyzeDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        return decks.AnalyzeDeckAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Creates the compact result returned by remote open operations.
    /// </summary>
    private static DeckOpenResult CreateOpenResult(DeckWorkspace workspace)
    {
        Dictionary<string, DeckCategory> categories = workspace.Categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return new DeckOpenResult
        {
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            Format = workspace.Format,
            Mode = workspace.Mode,
            WriteBack = workspace.WriteBack,
            ArchidektDeckId = workspace.ArchidektDeckId,
            SourceReferences = workspace.SourceReferences,
            Warnings = workspace.Warnings,
            Persistence = DeckPersistence.For(workspace),
            TotalCards = workspace.Cards.Sum(card => Math.Max(0, card.Quantity)),
            IncludedCards = workspace.Cards
                .Where(card => IsIncludedByPrimaryCategory(categories, card))
                .Sum(card => Math.Max(0, card.Quantity)),
            MaybeboardCards = workspace.Cards
                .Where(card => !IsIncludedByPrimaryCategory(categories, card))
                .Sum(card => Math.Max(0, card.Quantity)),
            Commanders = workspace.Cards
                .Where(card =>
                    card.PrimaryCategory.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)
                    || card.Categories.Contains(DeckRoles.Commander, StringComparer.OrdinalIgnoreCase))
                .Select(card => card.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Categories = workspace.Categories
                .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
                .Select(category => new DeckOpenCategorySummary
                {
                    Name = category.Name,
                    IncludedInDeck = category.IncludedInDeck,
                    CardCount = workspace.Cards
                        .Where(card => HasPrimaryCategory(card, category.Name))
                        .Sum(card => Math.Max(0, card.Quantity))
                })
                .ToList()
        };
    }

    /// <summary>
    /// Checks whether a card's primary category contributes to the active deck.
    /// </summary>
    private static bool IsIncludedByPrimaryCategory(
        IReadOnlyDictionary<string, DeckCategory> categories,
        DeckCard card)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        return !categories.TryGetValue(primaryCategory, out DeckCategory? category)
            || category.IncludedInDeck;
    }

    /// <summary>
    /// Checks whether a card's ordered primary category matches a category name.
    /// </summary>
    private static bool HasPrimaryCategory(DeckCard card, string category)
    {
        return DeckCategoryOrdering.PrimaryCategory(card)
            .Equals(category, StringComparison.OrdinalIgnoreCase);
    }
}
