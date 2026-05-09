using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

[McpServerToolType]
public sealed class WorkspaceTools
{
    private readonly DeckWorkspaceService decks;
    private readonly OperationModeGuard operationMode;

    public WorkspaceTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    [McpServerTool(Name = "create_local_deck", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create an offline local deck workspace. If local versus Archidekt is ambiguous, ask the user first or use start_deck_workspace.")]
    public Task<DeckWorkspace> CreateLocalDeckAsync(
        string name,
        string format = "commander",
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("create_local_deck");
        return decks.CreateLocalDeckAsync(name, format, description, cancellationToken);
    }

    [McpServerTool(Name = "start_deck_workspace", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Preferred first deck workspace tool. Requires explicit mode 'local' or 'archidekt'; if unclear, ask the user before calling. Archidekt mode also requires an explicit writeBack choice.")]
    public Task<DeckWorkspace> StartDeckWorkspaceAsync(
        string? mode = null,
        string? name = null,
        string format = "commander",
        string? description = null,
        string? archidektDeckIdOrUrl = null,
        bool? writeBack = null,
        string? decklist = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("start_deck_workspace");
        return decks.StartDeckWorkspaceAsync(
            mode,
            name,
            format,
            description,
            archidektDeckIdOrUrl,
            writeBack,
            decklist,
            cancellationToken);
    }

    [McpServerTool(Name = "list_local_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List saved local-only deck workspaces.")]
    public Task<IReadOnlyList<DeckWorkspace>> ListLocalDecksAsync(CancellationToken cancellationToken = default)
    {
        return decks.ListLocalWorkspacesAsync(cancellationToken);
    }

    [McpServerTool(Name = "open_local_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Open a saved local or cached Archidekt workspace by workspace id.")]
    public Task<DeckWorkspace> OpenLocalDeckAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        return decks.OpenLocalDeckAsync(workspaceId, cancellationToken);
    }

    [McpServerTool(Name = "open_archidekt_deck", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Open an Archidekt deck by id or URL. Requires explicit writeBack true or false; ask the user when writeback intent is unclear.")]
    public Task<DeckWorkspace> OpenArchidektDeckAsync(
        string deckIdOrUrl,
        bool? writeBack = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("open_archidekt_deck");
        if (!writeBack.HasValue)
        {
            throw new InvalidOperationException("Archidekt writeback intent is ambiguous. Ask the user whether edits should write back to Archidekt or stay local-only.");
        }

        return decks.OpenArchidektDeckAsync(deckIdOrUrl, writeBack.Value, cancellationToken);
    }

    [McpServerTool(Name = "list_archidekt_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("List decks visible to the configured Archidekt credentials.")]
    public Task<IReadOnlyList<ArchidektDeckSummary>> ListArchidektDecksAsync(CancellationToken cancellationToken = default)
    {
        return decks.ListArchidektDecksAsync(cancellationToken);
    }

    [McpServerTool(Name = "import_decklist", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Parse and import a decklist into a new local workspace.")]
    public Task<DeckWorkspace> ImportDecklistAsync(
        string decklist,
        string name,
        string format = "commander",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("import_decklist");
        return decks.ImportDecklistAsync(decklist, name, format, cancellationToken);
    }

    [McpServerTool(Name = "export_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Export a deck workspace as a grouped text decklist.")]
    public Task<string> ExportDeckAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        return decks.ExportDeckAsync(workspaceId, cancellationToken);
    }

    [McpServerTool(Name = "parse_decklist", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Parse a decklist without saving it.")]
    public ParsedDecklist ParseDecklist(string decklist)
    {
        return DeckWorkspaceService.ParseDecklist(decklist);
    }

    [McpServerTool(Name = "validate_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Validate workspace deck rules with lightweight format checks.")]
    public Task<DeckValidationResult> ValidateDeckAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        return decks.ValidateDeckAsync(workspaceId, cancellationToken);
    }

    [McpServerTool(Name = "analyze_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze category counts, type counts, color identity, curve, and metadata gaps.")]
    public Task<DeckAnalysis> AnalyzeDeckAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        return decks.AnalyzeDeckAsync(workspaceId, cancellationToken);
    }
}
