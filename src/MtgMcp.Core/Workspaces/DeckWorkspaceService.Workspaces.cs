namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Lists the local workspaces.
    /// </summary>
    public async Task<IReadOnlyList<DeckWorkspace>> ListLocalWorkspacesAsync(
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<DeckWorkspace> workspaces = await Repository
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        return workspaces.Where(workspace => workspace.Mode == WorkspaceMode.Local).ToList();
    }

    /// <summary>
    /// Creates the local deck.
    /// </summary>
    public async Task<DeckWorkspace> CreateLocalDeckAsync(
        string name,
        string format,
        string? description,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled Deck" : name.Trim(),
            Format = string.IsNullOrWhiteSpace(format) ? "commander" : format.Trim(),
            Description = description,
            Mode = WorkspaceMode.Local,
            WriteBack = false,
        };

        return await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a workspace from an explicit local, Archidekt, or Moxfield mode.
    /// </summary>
    public async Task<DeckWorkspace> StartDeckWorkspaceAsync(
        string? mode,
        string? name,
        string format,
        string? description,
        string? archidektDeckIdOrUrl,
        string? moxfieldDeckIdOrUrl,
        bool? writeBack,
        string? decklist,
        CancellationToken cancellationToken
    )
    {
        string normalizedMode = mode?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedMode))
        {
            throw new InvalidOperationException(
                "Workspace mode is ambiguous. Ask the user whether to use a local workspace or an Archidekt deck."
            );
        }

        if (normalizedMode is "local")
        {
            if (!string.IsNullOrWhiteSpace(archidektDeckIdOrUrl))
            {
                throw new InvalidOperationException(
                    "Local workspace mode cannot use an Archidekt deck id or URL. "
                        + "Ask the user whether they meant Archidekt instead."
                );
            }

            if (!string.IsNullOrWhiteSpace(moxfieldDeckIdOrUrl))
            {
                throw new InvalidOperationException(
                    "Local workspace mode cannot use a Moxfield deck id or URL. "
                        + "Use mode 'moxfield' to import a Moxfield deck as a local workspace."
                );
            }

            string deckName = string.IsNullOrWhiteSpace(name) ? "Untitled Deck" : name;
            if (!string.IsNullOrWhiteSpace(decklist))
            {
                return await ImportDecklistAsync(decklist, deckName, format, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await CreateLocalDeckAsync(deckName, format, description, cancellationToken)
                .ConfigureAwait(false);
        }

        if (normalizedMode is "archidekt")
        {
            if (!string.IsNullOrWhiteSpace(decklist))
            {
                throw new InvalidOperationException(
                    "Archidekt workspace mode cannot import pasted deck text directly. "
                        + "Ask whether to create a local import or open an Archidekt deck."
                );
            }

            if (string.IsNullOrWhiteSpace(archidektDeckIdOrUrl))
            {
                throw new InvalidOperationException(
                    "Archidekt workspace mode requires an Archidekt deck id or URL. Ask the user for the deck to open."
                );
            }

            if (!writeBack.HasValue)
            {
                throw new InvalidOperationException(
                    "Archidekt writeback intent is ambiguous. "
                        + "Ask the user whether edits should write back to Archidekt or stay local-only."
                );
            }

            return await OpenArchidektDeckAsync(
                    archidektDeckIdOrUrl,
                    writeBack.Value,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (normalizedMode is "moxfield")
        {
            if (!string.IsNullOrWhiteSpace(decklist))
            {
                throw new InvalidOperationException(
                    "Moxfield workspace mode cannot import pasted deck text directly. "
                        + "Use local mode for pasted deck text."
                );
            }

            if (!string.IsNullOrWhiteSpace(archidektDeckIdOrUrl))
            {
                throw new InvalidOperationException(
                    "Moxfield workspace mode cannot use an Archidekt deck id or URL."
                );
            }

            if (writeBack == true)
            {
                throw new InvalidOperationException(
                    "Moxfield writeback is not supported. Moxfield decks import as local-only workspaces."
                );
            }

            if (string.IsNullOrWhiteSpace(moxfieldDeckIdOrUrl))
            {
                throw new InvalidOperationException(
                    "Moxfield workspace mode requires a Moxfield deck id or URL."
                );
            }

            return await ImportMoxfieldDeckAsync(moxfieldDeckIdOrUrl, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Workspace mode must be 'local', 'archidekt', or 'moxfield'. Ask the user which workspace mode to use."
        );
    }

    /// <summary>
    /// Opens the local deck.
    /// </summary>
    public async Task<DeckWorkspace> OpenLocalDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace;
    }

    /// <summary>
    /// Opens the archidekt deck.
    /// </summary>
    public async Task<DeckWorkspace> OpenArchidektDeckAsync(
        string deckIdOrUrl,
        bool writeBack,
        CancellationToken cancellationToken
    )
    {
        IArchidektGateway gateway = RequireArchidektGateway();
        DeckWorkspace workspace = await gateway
            .ImportDeckAsync(deckIdOrUrl, writeBack, cancellationToken)
            .ConfigureAwait(false);
        await NormalizeWorkspaceCardsAsync(workspace, "missing", cancellationToken)
            .ConfigureAwait(false);

        return await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reopens an Archidekt-sourced workspace with writeback enabled using its explicit source reference.
    /// </summary>
    public async Task<DeckWorkspace> ReopenWorkspaceWithWritebackAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace existing = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string deckIdOrUrl = ResolveArchidektSourceReference(existing);
        return await OpenArchidektDeckAsync(deckIdOrUrl, writeBack: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Imports a Moxfield deck as a generic local workspace.
    /// </summary>
    public async Task<DeckWorkspace> ImportMoxfieldDeckAsync(
        string deckIdOrUrl,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await RequireMoxfieldGateway()
            .ImportDeckAsync(deckIdOrUrl, cancellationToken)
            .ConfigureAwait(false);
        await NormalizeWorkspaceCardsAsync(workspace, "missing", cancellationToken)
            .ConfigureAwait(false);

        workspace.Mode = WorkspaceMode.Local;
        workspace.WriteBack = false;
        return await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the archidekt decks.
    /// </summary>
    public Task<IReadOnlyList<ArchidektDeckSummary>> ListArchidektDecksAsync(
        CancellationToken cancellationToken
    )
    {
        return RequireArchidektGateway().ListDecksAsync(cancellationToken);
    }

    /// <summary>
    /// Parses the decklist.
    /// </summary>
    public static ParsedDecklist ParseDecklist(string decklist)
    {
        return DeckParser.Parse(decklist);
    }

    /// <summary>
    /// Imports the decklist.
    /// </summary>
    public async Task<DeckWorkspace> ImportDecklistAsync(
        string decklist,
        string name,
        string format,
        CancellationToken cancellationToken
    )
    {
        ParsedDecklist parsed = DeckParser.Parse(decklist);
        DeckWorkspace workspace = await CreateLocalDeckAsync(
                name,
                format,
                description: null,
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (ParsedDecklistLine line in parsed.Cards)
        {
            EnsureCategory(workspace, line.Category);
            workspace.Cards.Add(new DeckCard
            {
                Name = line.Name.Trim(),
                Quantity = Math.Max(1, line.Quantity),
                PrimaryCategory = line.Category,
                Categories = [line.Category],
            });
        }

        await NormalizeWorkspaceCardsAsync(workspace, "missing", cancellationToken).ConfigureAwait(false);
        return await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports the deck.
    /// </summary>
    public async Task<string> ExportDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        return await ExportDeckAsync(
                workspaceId,
                format: "text",
                includedOnly: false,
                includeCategories: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Exports the deck using caller-selected rendering options.
    /// </summary>
    public async Task<string> ExportDeckAsync(
        string workspaceId,
        string format,
        bool includedOnly,
        bool includeCategories,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return DeckExporter.Export(workspace, new DeckExportOptions
        {
            Format = format,
            IncludedOnly = includedOnly,
            IncludeCategories = includeCategories
        });
    }

    /// <summary>
    /// Validates the deck.
    /// </summary>
    public async Task<DeckValidationResult> ValidateDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return DeckValidator.Validate(workspace);
    }

    /// <summary>
    /// Analyzes the deck.
    /// </summary>
    public async Task<DeckAnalysis> AnalyzeDeckAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return DeckAnalyzer.Analyze(workspace);
    }

    /// <summary>
    /// Builds compact reusable state for a saved workspace.
    /// </summary>
    public async Task<DeckWorkspaceState> GetWorkspaceStateAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return BuildWorkspaceState(workspace);
    }

    /// <summary>
    /// Builds assistant-facing context from compact state and existing deck intent storage.
    /// </summary>
    public async Task<DeckAssistantContext> GetAssistantContextAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspaceState state = await GetWorkspaceStateAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckIntentResult intent = await GetDeckIntentAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return new DeckAssistantContext
        {
            State = state,
            Intent = intent
        };
    }

    /// <summary>
    /// Updates the deck metadata.
    /// </summary>
    public async Task<DeckChangeResult> UpdateDeckMetadataAsync(
        string workspaceId,
        string? name,
        string? format,
        string? description,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);

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
            await RequireArchidektGateway()
                .PersistMetadataAsync(workspace, cancellationToken)
                .ConfigureAwait(false);
        }

        await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return Change(workspace, DeckMutationKind.MetadataChanged, "Updated deck metadata.");
    }

    /// <summary>
    /// Builds compact state from a loaded workspace.
    /// </summary>
    internal static DeckWorkspaceState BuildWorkspaceState(DeckWorkspace workspace)
    {
        DeckAnalysis analysis = DeckAnalyzer.Analyze(workspace);
        DeckValidationResult validation = DeckValidator.Validate(workspace);
        DeckWorkspaceState state = new()
        {
            WorkspaceId = workspace.Id,
            WorkspaceResourceUri = $"mtg://workspace/{workspace.Id}",
            Name = workspace.Name,
            Format = workspace.Format,
            Persistence = DeckPersistence.For(workspace),
            IncludedCount = analysis.IncludedCards,
            CategoryCounts = new Dictionary<string, int>(analysis.CategoryCounts, StringComparer.OrdinalIgnoreCase),
            RoleCounts = new Dictionary<string, int>(analysis.RoleCounts, StringComparer.OrdinalIgnoreCase),
            Validation = validation
        };

        foreach (DeckCard card in workspace.Cards)
        {
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
            bool included = DeckCategoryInclusion.IsIncludedInDeck(workspace, card);
            bool creature = (snapshot.TypeLine ?? "").Contains("Creature", StringComparison.OrdinalIgnoreCase);

            if (role.PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase))
            {
                state.Commanders.Add(card.Name);
            }

            if (included && !creature && !role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            {
                state.ActiveNoncreatureSpells += Math.Max(0, card.Quantity);
            }

            DeckWorkspaceStateCard row = new()
            {
                CardName = card.Name,
                Quantity = card.Quantity,
                PrimaryCategory = primaryCategory,
                ManaValue = snapshot.ManaValue,
                TypeLine = snapshot.TypeLine,
                ScryfallUri = snapshot.ScryfallUri
            };

            if (included && snapshot.ManaValue >= 6)
            {
                state.HighManaValueCards.Add(row);
            }

            if (primaryCategory.Equals(DeckDefaults.Sideboard, StringComparison.OrdinalIgnoreCase))
            {
                state.SideboardCards.Add(row);
            }
            else if (primaryCategory.Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase))
            {
                state.MaybeboardCards.Add(row);
            }
        }

        state.Commanders = state.Commanders
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        state.HighManaValueCards.Sort(CompareStateCardsByManaValueThenName);
        state.SideboardCards.Sort(CompareStateCardsByName);
        state.MaybeboardCards.Sort(CompareStateCardsByName);
        state.TopWarnings.AddRange(validation.Errors.Take(3));
        state.TopWarnings.AddRange(validation.Warnings.Take(Math.Max(0, 5 - state.TopWarnings.Count)));
        state.TopWarnings.AddRange(workspace.Warnings.Take(Math.Max(0, 5 - state.TopWarnings.Count)));
        return state;
    }

    /// <summary>
    /// Sorts state cards by descending mana value, then name.
    /// </summary>
    private static int CompareStateCardsByManaValueThenName(
        DeckWorkspaceStateCard left,
        DeckWorkspaceStateCard right)
    {
        int manaValue = Nullable.Compare(right.ManaValue, left.ManaValue);
        return manaValue != 0
            ? manaValue
            : CompareStateCardsByName(left, right);
    }

    /// <summary>
    /// Sorts state cards by name.
    /// </summary>
    private static int CompareStateCardsByName(DeckWorkspaceStateCard left, DeckWorkspaceStateCard right)
    {
        return string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the Archidekt deck id or URL for writeback reopen operations.
    /// </summary>
    private static string ResolveArchidektSourceReference(DeckWorkspace workspace)
    {
        if (workspace.Mode == WorkspaceMode.Archidekt && !string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            return workspace.ArchidektDeckId;
        }

        foreach (DeckSourceReference reference in workspace.SourceReferences)
        {
            if (!reference.Provider.Equals(DeckImportProviders.Archidekt, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(reference.Url))
            {
                return reference.Url;
            }

            if (!string.IsNullOrWhiteSpace(reference.ExternalId))
            {
                return reference.ExternalId;
            }
        }

        throw new InvalidOperationException(
            "workspace_reopen_with_writeback requires an Archidekt-sourced workspace with a clear Archidekt deck id or URL."
        );
    }

    /// <summary>
    /// Loads the full workspace for MCP resource serialization.
    /// </summary>
    public async Task<DeckWorkspace> GetDeckResourceAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        return await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the compact workspace summary used by MCP resources.
    /// </summary>
    public async Task<object> GetDeckSummaryAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
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
            validation.Warnings,
        };
    }
}
