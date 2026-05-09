namespace MtgMcp.Core;

public sealed partial class DeckWorkspaceService
{
    public async Task<IReadOnlyList<DeckWorkspace>> ListLocalWorkspacesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DeckWorkspace> workspaces = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return workspaces
            .Where(workspace => workspace.Mode == WorkspaceMode.Local)
            .ToList();
    }

    public async Task<DeckWorkspace> CreateLocalDeckAsync(
        string name,
        string format,
        string? description,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled Deck" : name.Trim(),
            Format = string.IsNullOrWhiteSpace(format) ? "commander" : format.Trim(),
            Description = description,
            Mode = WorkspaceMode.Local,
            WriteBack = false
        };

        return await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeckWorkspace> StartDeckWorkspaceAsync(
        string? mode,
        string? name,
        string format,
        string? description,
        string? archidektDeckIdOrUrl,
        bool? writeBack,
        string? decklist,
        CancellationToken cancellationToken)
    {
        string normalizedMode = mode?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedMode))
        {
            throw new InvalidOperationException("Workspace mode is ambiguous. Ask the user whether to use a local workspace or an Archidekt deck.");
        }

        if (normalizedMode is "local")
        {
            if (!string.IsNullOrWhiteSpace(archidektDeckIdOrUrl))
            {
                throw new InvalidOperationException("Local workspace mode cannot use an Archidekt deck id or URL. Ask the user whether they meant Archidekt instead.");
            }

            string deckName = string.IsNullOrWhiteSpace(name) ? "Untitled Deck" : name;
            if (!string.IsNullOrWhiteSpace(decklist))
            {
                return await ImportDecklistAsync(decklist, deckName, format, cancellationToken).ConfigureAwait(false);
            }

            return await CreateLocalDeckAsync(deckName, format, description, cancellationToken).ConfigureAwait(false);
        }

        if (normalizedMode is "archidekt")
        {
            if (!string.IsNullOrWhiteSpace(decklist))
            {
                throw new InvalidOperationException("Archidekt workspace mode cannot import pasted deck text directly. Ask whether to create a local import or open an Archidekt deck.");
            }

            if (string.IsNullOrWhiteSpace(archidektDeckIdOrUrl))
            {
                throw new InvalidOperationException("Archidekt workspace mode requires an Archidekt deck id or URL. Ask the user for the deck to open.");
            }

            if (!writeBack.HasValue)
            {
                throw new InvalidOperationException("Archidekt writeback intent is ambiguous. Ask the user whether edits should write back to Archidekt or stay local-only.");
            }

            return await OpenArchidektDeckAsync(archidektDeckIdOrUrl, writeBack.Value, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Workspace mode must be either 'local' or 'archidekt'. Ask the user which workspace mode to use.");
    }

    public async Task<DeckWorkspace> OpenLocalDeckAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    public async Task<DeckWorkspace> OpenArchidektDeckAsync(
        string deckIdOrUrl,
        bool writeBack,
        CancellationToken cancellationToken)
    {
        IArchidektGateway gateway = RequireArchidektGateway();
        DeckWorkspace workspace = await gateway.ImportDeckAsync(deckIdOrUrl, writeBack, cancellationToken).ConfigureAwait(false);
        await NormalizeWorkspaceCardsAsync(workspace, "missing", cancellationToken).ConfigureAwait(false);
        return await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ArchidektDeckSummary>> ListArchidektDecksAsync(CancellationToken cancellationToken)
    {
        return RequireArchidektGateway().ListDecksAsync(cancellationToken);
    }

    public static ParsedDecklist ParseDecklist(string decklist)
    {
        return DeckParser.Parse(decklist);
    }

    public async Task<DeckWorkspace> ImportDecklistAsync(
        string decklist,
        string name,
        string format,
        CancellationToken cancellationToken)
    {
        ParsedDecklist parsed = DeckParser.Parse(decklist);
        DeckWorkspace workspace = await CreateLocalDeckAsync(name, format, description: null, cancellationToken).ConfigureAwait(false);

        foreach (ParsedDecklistLine line in parsed.Cards)
        {
            EnsureCategory(workspace, line.Category);
            DeckCard card = await CreateDeckCardAsync(line.Name, line.Quantity, line.Category, cancellationToken).ConfigureAwait(false);
            workspace.Cards.Add(card);
        }

        await NormalizeWorkspaceCardsAsync(workspace, "missing", cancellationToken).ConfigureAwait(false);
        return await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExportDeckAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return DeckExporter.Export(workspace);
    }

    public async Task<DeckValidationResult> ValidateDeckAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return DeckValidator.Validate(workspace);
    }

    public async Task<DeckAnalysis> AnalyzeDeckAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return DeckAnalyzer.Analyze(workspace);
    }

    public async Task<DeckChangeResult> UpdateDeckMetadataAsync(
        string workspaceId,
        string? name,
        string? format,
        string? description,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(name))
        {
            workspace.Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(format))
        {
            workspace.Format = format.Trim();
        }

        if (description is not null)
        {
            workspace.Description = description;
        }

        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack)
        {
            await RequireArchidektGateway().PersistMetadataAsync(workspace, cancellationToken).ConfigureAwait(false);
        }

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return Change(workspace, DeckMutationKind.MetadataChanged, "Updated deck metadata.");
    }

    public async Task<DeckWorkspace> GetDeckResourceAsync(string workspaceId, CancellationToken cancellationToken)
    {
        return await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<object> GetDeckSummaryAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckAnalysis deckAnalysis = DeckAnalyzer.Analyze(workspace);
        DeckValidationResult validation = DeckValidator.Validate(workspace);
        return new
        {
            workspace.Id,
            workspace.Name,
            workspace.Format,
            workspace.Mode,
            workspace.WriteBack,
            Persistence = DeckPersistence.For(workspace),
            deckAnalysis.TotalCards,
            deckAnalysis.IncludedCards,
            Categories = workspace.Categories.Select(category => category.Name).ToArray(),
            validation.IsValid,
            validation.Errors,
            validation.Warnings
        };
    }
}
