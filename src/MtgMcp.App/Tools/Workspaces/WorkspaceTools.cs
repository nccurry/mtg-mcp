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
    /// Creates the MCP workspace lifecycle tool group.
    /// </summary>
    public WorkspaceTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Creates the local deck.
    /// </summary>
    public Task<DeckWorkspace> CreateLocalDeckAsync(
        string name,
        string format = "commander",
        string? description = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("workspace_start");
        return decks.CreateLocalDeckAsync(name, format, description, cancellationToken);
    }

    /// <summary>
    /// Starts a workspace from the explicitly selected source mode.
    /// </summary>
    [McpServerTool(
        Name = "workspace_start",
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
        [Description("Workspace source mode: local, archidekt, or moxfield.")]
        string? mode = null,
        string? name = null,
        string format = "commander",
        string? description = null,
        [Description("Archidekt deck id or URL when mode is archidekt.")]
        string? archidektDeckIdOrUrl = null,
        [Description("Moxfield deck id or URL when mode is moxfield.")]
        string? moxfieldDeckIdOrUrl = null,
        [Description("Required for Archidekt mode: true to persist edits to Archidekt, false to keep a local cached workspace.")]
        bool? writeBack = null,
        string? decklist = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("workspace_start");
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
        Name = "workspace_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("List saved local deck workspaces.")]
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
        Name = "workspace_open",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Open a saved workspace by workspace id.")]
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
    public async Task<DeckOpenResult> OpenArchidektDeckAsync(
        string deckIdOrUrl,
        bool? writeBack = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("workspace_start");
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
    /// Reopens an Archidekt-sourced workspace with writeback enabled.
    /// </summary>
    [McpServerTool(
        Name = "workspace_reopen_with_writeback",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Reopen an Archidekt-sourced workspace with writeback enabled using its explicit source deck id or URL.")]
    public async Task<DeckOpenResult> ReopenWorkspaceWithWritebackAsync(
        string workspaceId,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("workspace_reopen_with_writeback");
        DeckWorkspace workspace = await decks.ReopenWorkspaceWithWritebackAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return CreateOpenResult(workspace);
    }

    /// <summary>
    /// Imports a Moxfield deck.
    /// </summary>
    public async Task<DeckOpenResult> OpenMoxfieldDeckAsync(
        string deckIdOrUrl,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("workspace_start");
        DeckWorkspace workspace = await decks.ImportMoxfieldDeckAsync(deckIdOrUrl, cancellationToken)
            .ConfigureAwait(false);
        return CreateOpenResult(workspace);
    }

    /// <summary>
    /// Creates an Archidekt deck.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_create_deck",
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
        operationMode.EnsureCanMutate("archidekt_create_deck");
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
        Name = "archidekt_copy_workspace",
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
            operationMode.EnsureCanWritePlanningState("archidekt_copy_workspace");
        }
        else
        {
            operationMode.EnsureCanMutate("archidekt_copy_workspace");
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
        Name = "archidekt_list_decks",
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
    public Task<DeckWorkspace> ImportDecklistAsync(
        string decklist,
        string name,
        string format = "commander",
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("workspace_start");
        return decks.ImportDecklistAsync(decklist, name, format, cancellationToken);
    }

    /// <summary>
    /// Exports the deck.
    /// </summary>
    [McpServerTool(
        Name = "workspace_export",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Export a deck workspace as a grouped decklist. format supports text, markdown, and markdown-links; defaults preserve grouped text output.")]
    public Task<string> ExportDeckAsync(
        string workspaceId,
        [Description("Export format: text, markdown, or markdown-links.")]
        string format = "text",
        [Description("When true, export only cards in categories included in the active deck.")]
        bool includedOnly = false,
        [Description("When true, group cards by category; keep true to preserve current grouped text behavior.")]
        bool includeCategories = true,
        CancellationToken cancellationToken = default
    )
    {
        return decks.ExportDeckAsync(workspaceId, format, includedOnly, includeCategories, cancellationToken);
    }

    /// <summary>
    /// Parses the decklist.
    /// </summary>
    [McpServerTool(
        Name = "workspace_parse_decklist",
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
        Name = "workspace_validate",
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
    /// Compares two explicitly selected saved workspaces.
    /// </summary>
    [McpServerTool(
        Name = "workspace_diff",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Compare a workspace against an explicit baseline workspace id; baseline id/source/timestamp are returned prominently.")]
    public Task<WorkspaceDiffResult> DiffWorkspacesAsync(
        string workspaceId,
        [Description("Explicit baseline workspace id to compare against. Hidden latest-baseline selection is not used.")]
        string previousWorkspaceId,
        CancellationToken cancellationToken = default
    )
    {
        return decks.DiffWorkspacesAsync(workspaceId, previousWorkspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes the deck.
    /// </summary>
    [McpServerTool(
        Name = "deck_analyze_structure",
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
