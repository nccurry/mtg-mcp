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
    public async Task<object> StartDeckWorkspaceAsync(
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
        [Description("Output detail level: summary, normal, or full. Default summary returns workspace id, counts, commanders, source, and writeback status; full returns the raw workspace.")]
        string detailLevel = "summary",
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("workspace_start");
        DeckWorkspace workspace = await decks
            .StartDeckWorkspaceAsync(
                mode,
                name,
                format,
                description,
                archidektDeckIdOrUrl,
                moxfieldDeckIdOrUrl,
                writeBack,
                decklist,
                cancellationToken)
            .ConfigureAwait(false);
        string normalizedDetailLevel = NormalizeWorkspaceStartDetailLevel(detailLevel);
        return normalizedDetailLevel == DetailLevelParser.Full
            ? workspace
            : CreateOpenResult(workspace, normalizedDetailLevel);
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
    [Description("List compact saved local deck workspace summaries without card snapshots; use workspace_open for full cards. Supports cursor paging with limit and nextCursor.")]
    public async Task<PagedToolResult<DeckWorkspaceSummary>> ListLocalDecksAsync(
        [Description("Maximum rows to return, clamped to 1-200.")]
        int limit = 50,
        [Description("Opaque cursor from a previous workspace_list nextCursor.")]
        string? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<DeckWorkspace> workspaces = await decks
            .ListLocalWorkspacesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToolPagination.Page(workspaces.Select(CreateWorkspaceSummary).ToList(), limit, cursor);
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
    /// Refreshes a provider-sourced workspace in place.
    /// </summary>
    [McpServerTool(
        Name = "workspace_refresh_from_source",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Refresh an existing provider-sourced workspace in place from Archidekt or Moxfield, preserving the workspace id and recording a last-import diff baseline.")]
    public async Task<object> RefreshWorkspaceFromSourceAsync(
        string workspaceId,
        [Description("Optional Archidekt writeback override. Moxfield refresh always remains local-only.")]
        bool? writeBack = null,
        [Description("Output detail level: summary, normal, or full. Default summary omits the raw workspace payload.")]
        string detailLevel = "summary",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("workspace_refresh_from_source");
        WorkspaceRefreshFromSourceResult result = await decks
            .RefreshWorkspaceFromSourceAsync(workspaceId, writeBack, cancellationToken)
            .ConfigureAwait(false);
        string normalizedDetailLevel = NormalizeWorkspaceStartDetailLevel(detailLevel);
        if (normalizedDetailLevel == DetailLevelParser.Full)
        {
            return result;
        }

        return new
        {
            result.Status,
            result.WorkspaceId,
            result.Provider,
            result.ExternalId,
            result.LocalWorkspaceId,
            Workspace = result.Workspace is null
                ? null
                : CreateOpenResult(result.Workspace, normalizedDetailLevel),
            DiffLastImport = result.DiffLastImport,
            result.Notes
        };
    }

    /// <summary>
    /// Lists workspace cards by active or excluded zone.
    /// </summary>
    [McpServerTool(
        Name = "deck_list_cards_by_zone",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("List compact workspace cards by zone: active, sideboard, maybeboard, excluded, or all. collapseDuplicates=true merges duplicate card rows.")]
    public Task<DeckCardsByZoneResult> ListCardsByZoneAsync(
        string workspaceId,
        [Description("Card zone: active, sideboard, maybeboard, excluded, or all.")]
        string zone = DeckCardZones.Active,
        bool collapseDuplicates = true,
        CancellationToken cancellationToken = default)
    {
        return decks.ListCardsByZoneAsync(workspaceId, zone, collapseDuplicates, cancellationToken);
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
        string? parentFolderId = null,
        string? folderName = null,
        CancellationToken cancellationToken = default
    )
    {
        operationMode.EnsureCanMutate("archidekt_create_deck");
        DeckWorkspace workspace = await decks.CreateArchidektDeckAsync(
                name,
                format,
                description,
                visibility,
                parentFolderId,
                folderName,
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
        [Description("When omitted, destinationDeckIdOrUrl selects existing-deck mode; otherwise a new deck is created.")]
        bool? createNew = null,
        string? destinationDeckIdOrUrl = null,
        string? name = null,
        string? format = null,
        string? description = null,
        string visibility = "private",
        bool allowNonEmptyDestination = false,
        bool replaceExistingDestination = false,
        string? parentFolderId = null,
        string? folderName = null,
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

        bool effectiveCreateNew = createNew ?? string.IsNullOrWhiteSpace(destinationDeckIdOrUrl);
        return decks.CopyWorkspaceToArchidektAsync(
            workspaceId,
            dryRun,
            effectiveCreateNew,
            destinationDeckIdOrUrl,
            name,
            format,
            description,
            visibility,
            allowNonEmptyDestination,
            replaceExistingDestination,
            parentFolderId,
            folderName,
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
        int? page = null,
        int? pageSize = null,
        string? folderId = null,
        string? folderName = null,
        CancellationToken cancellationToken = default
    )
    {
        return decks.ListArchidektDecksAsync(
            new ArchidektDeckListRequest
            {
                Page = page,
                PageSize = pageSize,
                FolderId = folderId,
                FolderName = folderName
            },
            cancellationToken);
    }

    /// <summary>
    /// Lists Archidekt folders.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_list_folders",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("List folders visible to the configured Archidekt credentials.")]
    public Task<IReadOnlyList<ArchidektFolder>> ListArchidektFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        return decks.ListArchidektFoldersAsync(cancellationToken);
    }

    /// <summary>
    /// Creates an Archidekt folder.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_create_folder",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Create an Archidekt folder under an optional parent folder id.")]
    public Task<ArchidektFolder> CreateArchidektFolderAsync(
        string name,
        string? parentFolderId = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("archidekt_create_folder");
        return decks.CreateArchidektFolderAsync(name, parentFolderId, cancellationToken);
    }

    /// <summary>
    /// Moves Archidekt decks into a folder.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_move_decks",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true
    )]
    [Description("Move Archidekt decks into a folder. Omit folderId to move decks to the root.")]
    public Task<ArchidektMoveDecksResult> MoveArchidektDecksAsync(
        string[] deckIds,
        string? folderId = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("archidekt_move_decks");
        return decks.MoveArchidektDecksAsync(deckIds, folderId, cancellationToken);
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
    /// Runs a cached-metadata legality audit.
    /// </summary>
    [McpServerTool(
        Name = "workspace_validate_legality",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Validate cached card legality, Commander color identity, command-zone shape, copy limits, sideboard size, and metadata gaps without live lookups.")]
    public async Task<object> ValidateLegalityAsync(
        string workspaceId,
        [Description("Output detail level: summary, normal, or full.")]
        string detailLevel = "summary",
        bool includeExcluded = false,
        CancellationToken cancellationToken = default)
    {
        DeckLegalityAudit audit = await decks
            .ValidateLegalityAsync(workspaceId, includeExcluded, cancellationToken)
            .ConfigureAwait(false);
        string normalizedDetailLevel = NormalizeWorkspaceStartDetailLevel(detailLevel);
        return normalizedDetailLevel == DetailLevelParser.Full
            ? audit
            : SummarizeLegalityAudit(audit, normalizedDetailLevel);
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
    /// Compares a workspace against its previous provider import baseline.
    /// </summary>
    [McpServerTool(
        Name = "workspace_diff_last_import",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Compare a workspace against the previous import into the same provider, external deck id, and local workspace id. Status is baselineFound, noPriorBaseline, sourceUnsupported, workspaceHasNoSource, or historyUnavailable.")]
    public Task<WorkspaceDiffLastImportResult> DiffLastImportAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.DiffLastImportAsync(workspaceId, cancellationToken);
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
    /// Creates bounded legality audit output for summary and normal detail levels.
    /// </summary>
    private static object SummarizeLegalityAudit(DeckLegalityAudit audit, string detailLevel)
    {
        int limit = detailLevel.Equals(DetailLevelParser.Normal, StringComparison.OrdinalIgnoreCase)
            ? 25
            : 8;
        return new
        {
            DetailLevel = detailLevel,
            audit.WorkspaceId,
            audit.Format,
            audit.IncludeExcluded,
            audit.IsLegal,
            audit.IncludedCount,
            audit.AuditedCardRows,
            audit.CommandZone,
            ErrorCount = audit.Errors.Count,
            WarningCount = audit.Warnings.Count,
            CardLegalityIssueCount = audit.CardLegalityIssues.Count,
            ColorIdentityIssueCount = audit.ColorIdentityIssues.Count,
            CopyLimitIssueCount = audit.CopyLimitIssues.Count,
            SideboardIssueCount = audit.SideboardIssues.Count,
            MetadataGapCount = audit.MetadataGaps.Count,
            Errors = audit.Errors.Take(limit).ToList(),
            Warnings = audit.Warnings.Take(limit).ToList(),
            CardLegalityIssues = audit.CardLegalityIssues.Take(limit).ToList(),
            ColorIdentityIssues = audit.ColorIdentityIssues.Take(limit).ToList(),
            CopyLimitIssues = audit.CopyLimitIssues.Take(limit).ToList(),
            SideboardIssues = audit.SideboardIssues.Take(limit).ToList(),
            MetadataGaps = audit.MetadataGaps.Take(limit).ToList(),
            Assumptions = audit.Assumptions.Take(limit).ToList()
        };
    }

    /// <summary>
    /// Creates the compact result returned by remote open operations.
    /// </summary>
    private static DeckOpenResult CreateOpenResult(DeckWorkspace workspace)
    {
        return CreateOpenResult(workspace, DetailLevelParser.Summary);
    }

    /// <summary>
    /// Creates the compact result returned by workspace start and remote open operations.
    /// </summary>
    private static DeckOpenResult CreateOpenResult(DeckWorkspace workspace, string detailLevel)
    {
        Dictionary<string, DeckCategory> categories = workspace.Categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        bool includeCards = detailLevel.Equals(DetailLevelParser.Normal, StringComparison.OrdinalIgnoreCase);
        List<DeckOpenCardSummary> cards = [];
        if (includeCards)
        {
            foreach (DeckCard card in workspace.Cards)
            {
                cards.Add(new DeckOpenCardSummary
                {
                    CardName = card.Name,
                    Quantity = card.Quantity,
                    PrimaryCategory = DeckCategoryOrdering.PrimaryCategory(card),
                    Categories = card.Categories.ToList(),
                    TypeLine = card.Snapshot?.TypeLine,
                    ScryfallUri = card.Snapshot?.ScryfallUri
                });
            }
        }

        return new DeckOpenResult
        {
            DetailLevel = detailLevel,
            Id = workspace.Id,
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
                .ToList(),
            Cards = cards
        };
    }

    /// <summary>
    /// Creates the compact result returned by workspace list.
    /// </summary>
    private static DeckWorkspaceSummary CreateWorkspaceSummary(DeckWorkspace workspace)
    {
        Dictionary<string, DeckCategory> categories = workspace.Categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return new DeckWorkspaceSummary
        {
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            Format = workspace.Format,
            Mode = workspace.Mode,
            UpdatedAt = workspace.UpdatedAt,
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
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SourceReferences = workspace.SourceReferences,
            Warnings = workspace.Warnings
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

    /// <summary>
    /// Normalizes workspace_start detail levels.
    /// </summary>
    private static string NormalizeWorkspaceStartDetailLevel(string? detailLevel)
    {
        return DetailLevelParser.Normalize(detailLevel);
    }
}
