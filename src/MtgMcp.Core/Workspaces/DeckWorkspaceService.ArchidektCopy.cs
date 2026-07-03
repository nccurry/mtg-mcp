namespace MtgMcp.Core;

/// <summary>
/// Copies provider-neutral workspaces into Archidekt decks.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Mirrors the Archidekt adapter card mutation batch size for copy request estimates.
    /// </summary>
    private const int EstimatedArchidektCardMutationBatchSize = 50;

    /// <summary>
    /// Creates an empty Archidekt deck and stores the writeback workspace locally.
    /// </summary>
    public async Task<DeckWorkspace> CreateArchidektDeckAsync(
        string name,
        string format,
        string? description,
        string visibility,
        CancellationToken cancellationToken
    )
    {
        return await CreateArchidektDeckAsync(
                name,
                format,
                description,
                visibility,
                parentFolderId: null,
                folderName: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an empty Archidekt deck in an optional folder and stores the writeback workspace locally.
    /// </summary>
    public async Task<DeckWorkspace> CreateArchidektDeckAsync(
        string name,
        string format,
        string? description,
        string visibility,
        string? parentFolderId,
        string? folderName,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await RequireArchidektGateway()
            .CreateDeckAsync(
                new ArchidektDeckCreateRequest
                {
                    Name = name,
                    Format = format,
                    Description = description,
                    Visibility = visibility,
                    ParentFolderId = parentFolderId,
                    FolderName = folderName,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await Repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Previews or applies a full workspace copy into a new or existing Archidekt deck.
    /// </summary>
    public async Task<ArchidektCopyResult> CopyWorkspaceToArchidektAsync(
        string workspaceId,
        bool dryRun,
        bool createNew,
        string? destinationDeckIdOrUrl,
        string? name,
        string? format,
        string? description,
        string visibility,
        bool allowNonEmptyDestination,
        bool replaceExistingDestination,
        CancellationToken cancellationToken
    )
    {
        return await CopyWorkspaceToArchidektAsync(
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
                parentFolderId: null,
                folderName: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Previews or applies a full workspace copy into a new or existing Archidekt deck with optional folder placement.
    /// </summary>
    public async Task<ArchidektCopyResult> CopyWorkspaceToArchidektAsync(
        string workspaceId,
        bool dryRun,
        bool createNew,
        string? destinationDeckIdOrUrl,
        string? name,
        string? format,
        string? description,
        string visibility,
        bool allowNonEmptyDestination,
        bool replaceExistingDestination,
        string? parentFolderId,
        string? folderName,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace source = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        ArchidektCopyResult result = CreateCopyResult(
            source,
            dryRun,
            createNew,
            destinationDeckIdOrUrl,
            name);

        ValidateDestinationChoice(
            createNew,
            destinationDeckIdOrUrl,
            allowNonEmptyDestination,
            replaceExistingDestination);
        result.CopyPhase = "validated";

        string destinationName = string.IsNullOrWhiteSpace(name) ? source.Name : name.Trim();
        DeckWorkspace? destination = null;
        if (!string.IsNullOrWhiteSpace(destinationDeckIdOrUrl))
        {
            result.CopyPhase = "destination-read";
            destination = await RequireArchidektGateway()
                .ImportDeckAsync(destinationDeckIdOrUrl, writeBack: !dryRun, cancellationToken)
                .ConfigureAwait(false);
            result.DestinationArchidektDeckId = destination.ArchidektDeckId;
            result.DestinationName = destination.Name;
            AddDestinationWarnings(source, destination, result);
            if (destination.Cards.Count > 0 && !allowNonEmptyDestination && !replaceExistingDestination)
            {
                result.Warnings.Add(
                    "Destination Archidekt deck is not empty; set allowNonEmptyDestination=true to append cards "
                        + "or replaceExistingDestination=true to replace its cards."
                );
            }

            if (destination.Cards.Count > 0 && replaceExistingDestination)
            {
                result.Warnings.Add("Destination Archidekt deck cards will be replaced.");
            }
        }

        result.ExpectedCardRows = EstimateExpectedCardRows(
            source,
            destination,
            source.Cards,
            createNew,
            replaceExistingDestination);
        if (dryRun)
        {
            result.CopyPhase = "dry-run";
            UpdateDryRunCardIdDiagnostics(result);
            return result;
        }

        if (destination?.Cards.Count > 0 && !allowNonEmptyDestination && !replaceExistingDestination)
        {
            throw new InvalidOperationException(
                "Destination Archidekt deck is not empty. "
                    + "Set allowNonEmptyDestination=true to append cards intentionally "
                    + "or replaceExistingDestination=true to replace its cards."
            );
        }

        List<DeckCard> copiedCards = source.Cards.Select(CloneForArchidektCopy).ToList();

        if (createNew)
        {
            result.CopyPhase = "destination-discovery";
            destination = await FindExistingMigrationDestinationAsync(
                    source,
                    destinationName,
                    cancellationToken)
                .ConfigureAwait(false);
            if (destination is not null)
            {
                result.CreatedNewDeck = false;
                result.DestinationArchidektDeckId = destination.ArchidektDeckId;
                result.DestinationName = destination.Name;
                if (HasSameCopiedCards(source.Cards, destination.Cards))
                {
                    destination = await Repository.SaveAsync(destination, cancellationToken)
                        .ConfigureAwait(false);
                    result.DestinationWorkspaceId = destination.Id;
                    result.Warnings.Add(
                        "Found an existing Archidekt deck created from this source workspace; "
                            + "returning it instead of creating a duplicate."
                    );
                    result.CopyPhase = "complete";
                    return result;
                }

                if (destination.Cards.Count == 0)
                {
                    result.Warnings.Add(
                        "Found an empty Archidekt deck already created from this source workspace; "
                            + "reusing it instead of creating a duplicate."
                    );
                }
                else
                {
                    copiedCards = PreparePartialMigrationResume(source, destination, result, copiedCards);
                }
            }
        }

        if (replaceExistingDestination && destination is not null)
        {
            CopyKnownArchidektCardIds(destination.Cards, copiedCards);
        }

        result.ExpectedCardRows = EstimateExpectedCardRows(
            source,
            destination,
            copiedCards,
            createNew,
            replaceExistingDestination);
        result.CopyPhase = "preflight";
        await ResolveCopiedCardIdsBeforeMutationAsync(result, copiedCards, cancellationToken)
            .ConfigureAwait(false);
        if (result.MissingArchidektCardIds > 0)
        {
            BlockCopyBeforeMutation(
                result,
                "preflight",
                "Apply stopped before mutating Archidekt because one or more copied rows could not be resolved to Archidekt card ids.");
            return result;
        }

        try
        {
            if (createNew)
            {
                result.CopyPhase = "create-deck";
                destination ??= await CreateArchidektDeckAsync(
                        destinationName,
                        format ?? source.Format,
                        BuildMigrationDescription(description ?? source.Description, source),
                        visibility,
                        parentFolderId,
                        folderName,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                result.CopyPhase = "open-destination";
                destination = await OpenArchidektDeckAsync(
                        destinationDeckIdOrUrl
                            ?? throw new InvalidOperationException("Destination Archidekt deck id or URL is required."),
                        writeBack: true,
                        cancellationToken)
                    .ConfigureAwait(false);

                result.CopyPhase = "checkpoint";
                DeckCheckpoint checkpoint = await RequireArchidektGateway()
                    .CreateCheckpointAsync(
                        destination,
                        $"Before mtg-mcp copy {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                        "Created before mtg-mcp copied workspace cards into this Archidekt deck.",
                        cancellationToken)
                    .ConfigureAwait(false);
                result.CheckpointId = checkpoint.Id;
                if (string.IsNullOrWhiteSpace(result.CheckpointId))
                {
                    throw new InvalidOperationException("Archidekt checkpoint creation did not return a checkpoint id.");
                }
            }

            destination.Name = string.IsNullOrWhiteSpace(name) ? destination.Name : name.Trim();
            destination.Format = string.IsNullOrWhiteSpace(format) ? destination.Format : format.Trim();
            destination.Description = BuildMigrationDescription(
                SelectCopyDescription(description, source, destination, createNew),
                source);
            result.CopyPhase = "metadata";
            await RequireArchidektGateway()
                .PersistMetadataAsync(destination, cancellationToken)
                .ConfigureAwait(false);
            await Repository.SaveAsync(destination, cancellationToken).ConfigureAwait(false);

            result.CopyPhase = "categories";
            await CopyCategoriesToArchidektAsync(source, destination, cancellationToken)
                .ConfigureAwait(false);

            if (replaceExistingDestination && destination.Cards.Count > 0)
            {
                result.CopyPhase = "remove-cards";
                List<DeckCard> removedCards = destination.Cards.ToList();
                await PersistCardsAsync(destination, [], removedCards, cancellationToken)
                    .ConfigureAwait(false);
                destination.Cards.Clear();
            }

            destination.Cards.AddRange(copiedCards);
            result.CopyPhase = "add-cards";
            await PersistCardsAsync(destination, copiedCards, [], cancellationToken)
                .ConfigureAwait(false);
            result.WrittenRows = copiedCards.Count;
            UpdateCopyCardIdDiagnostics(result, copiedCards);

            result.DestinationWorkspaceId = destination.Id;
            result.DestinationArchidektDeckId = destination.ArchidektDeckId;
            result.DestinationName = destination.Name;
            if (!await VerifyArchidektCopyAsync(destination, destination.Cards, result, cancellationToken)
                    .ConfigureAwait(false))
            {
                return result;
            }

            result.CopyPhase = "complete";
            return result;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await PopulateCopyFailureAsync(
                    result,
                    destination,
                    createNew,
                    replaceExistingDestination,
                    exception,
                    cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>
    /// Validates a non-empty prior migration destination and prepares its missing card rows.
    /// </summary>
    private static List<DeckCard> PreparePartialMigrationResume(
        DeckWorkspace source,
        DeckWorkspace destination,
        ArchidektCopyResult result,
        List<DeckCard> copiedCards)
    {
        if (!HasSubsetOfCopiedCards(source.Cards, destination.Cards))
        {
            throw new InvalidOperationException(
                "Found an existing Archidekt deck created from this source workspace, "
                    + "but its cards do not match the source. Use destinationDeckIdOrUrl="
                    + $"{destination.ArchidektDeckId} with replaceExistingDestination=true "
                    + "to replace it intentionally.");
        }

        result.CanResume = true;
        result.ResumeDeckIdOrUrl = destination.ArchidektDeckId;
        result.NextAction =
            $"Resume with archidekt_copy_workspace destinationDeckIdOrUrl={destination.ArchidektDeckId}, "
            + "createNew=false, allowNonEmptyDestination=true.";
        result.Warnings.Add(
            "Found a partially copied Archidekt deck created from this source workspace; "
                + "resuming by writing only missing card rows.");
        return FilterAlreadyCopiedCards(copiedCards, destination.Cards);
    }

    /// <summary>
    /// Creates the shared report body for dry-run and apply responses.
    /// </summary>
    private static ArchidektCopyResult CreateCopyResult(
        DeckWorkspace source,
        bool dryRun,
        bool createNew,
        string? destinationDeckIdOrUrl,
        string? name
    )
    {
        Dictionary<string, DeckCategory> categories = source.Categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<string> warnings = source.Warnings.ToList();
        int missingScryfallIds = source.Cards.Count(card => string.IsNullOrWhiteSpace(card.ScryfallId));
        if (source.Cards.Count == 0)
        {
            warnings.Add("Source workspace has no cards to copy.");
        }

        if (missingScryfallIds > 0)
        {
            warnings.Add($"{missingScryfallIds} card(s) have no Scryfall id; Archidekt print matching may fall back to name.");
        }

        return new ArchidektCopyResult
        {
            DryRun = dryRun,
            SourceWorkspaceId = source.Id,
            DestinationArchidektDeckId = destinationDeckIdOrUrl,
            CreatedNewDeck = createNew,
            DestinationName = name ?? source.Name,
            TotalCards = source.Cards.Sum(card => Math.Max(0, card.Quantity)),
            IncludedCards = source.Cards
                .Where(card => IsIncludedByPrimaryCategory(categories, card))
                .Sum(card => Math.Max(0, card.Quantity)),
            CopyPhase = dryRun ? "dry-run" : "initialized",
            EstimatedArchidektRequests = EstimateArchidektCopyRequests(source, createNew, destinationDeckIdOrUrl),
            MissingArchidektCardIds = source.Cards.Count(card => string.IsNullOrWhiteSpace(card.ArchidektCardId)),
            CardIdDiagnostics = source.Cards.Any(card => string.IsNullOrWhiteSpace(card.ArchidektCardId))
                ? "Missing cached Archidekt card ids are cache misses. Apply mode will resolve them before card writes."
                : "All copied rows already have cached Archidekt card ids.",
            Categories = source.Categories
                .Select(category => category.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Commanders = source.Cards
                .Where(IsCommanderCard)
                .Select(card => card.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Coarse request budget shown in dry-run diagnostics before Archidekt is contacted.
    /// </summary>
    private static int EstimateArchidektCopyRequests(
        DeckWorkspace source,
        bool createNew,
        string? destinationDeckIdOrUrl)
    {
        int requests = 0;
        if (createNew)
        {
            requests += 2;
        }

        if (!string.IsNullOrWhiteSpace(destinationDeckIdOrUrl))
        {
            requests++;
        }

        requests += 1;
        requests += source.Categories.Count;
        requests += source.Cards.Count == 0
            ? 0
            : (int)Math.Ceiling(source.Cards.Count / (double)EstimatedArchidektCardMutationBatchSize);
        requests += source.Cards
            .Where(card => string.IsNullOrWhiteSpace(card.ArchidektCardId))
            .Select(GetCopyResolutionEstimateKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return requests;
    }

    /// <summary>
    /// Builds the unique-print key used for request estimates.
    /// </summary>
    private static string GetCopyResolutionEstimateKey(DeckCard card)
    {
        string printKey = MtgMcpText.FirstNonEmpty(
                card.ScryfallId,
                string.IsNullOrWhiteSpace(card.Snapshot.Set)
                    || string.IsNullOrWhiteSpace(card.Snapshot.CollectorNumber)
                    ? null
                    : $"{card.Snapshot.Set}:{card.Snapshot.CollectorNumber}",
                card.Name)
            ?? "";
        return $"{card.Name}|{printKey}";
    }

    /// <summary>
    /// Updates card-id diagnostics after the Archidekt adapter resolves missing ids.
    /// </summary>
    private static void UpdateCopyCardIdDiagnostics(
        ArchidektCopyResult result,
        IReadOnlyList<DeckCard> cards)
    {
        result.CardIdCacheHits = cards.Count(card =>
            card.Metadata.TryGetValue(DeckCardMetadataKeys.ArchidektCardIdResolution, out string? value)
            && value.Equals("cache", StringComparison.OrdinalIgnoreCase));
        result.CardIdsResolved = cards.Count(card =>
            card.Metadata.TryGetValue(DeckCardMetadataKeys.ArchidektCardIdResolution, out string? value)
            && value.Equals("resolved", StringComparison.OrdinalIgnoreCase));
        result.CacheHits = result.CardIdCacheHits;
        result.RemoteLookups = result.CardIdsResolved;
        result.ResolvedCount = cards.Count(card => !string.IsNullOrWhiteSpace(card.ArchidektCardId));
        result.MissingArchidektCardIds = cards.Count(card => string.IsNullOrWhiteSpace(card.ArchidektCardId));
        result.CardIdDiagnostics = result.MissingArchidektCardIds == 0
            ? $"Resolved Archidekt card ids for {result.ResolvedCount} copied row(s)."
            : $"{result.MissingArchidektCardIds} copied row(s) still lacked Archidekt card ids after resolution.";
    }

    /// <summary>
    /// Clarifies that dry-run card-id gaps are cache misses, not a predicted apply failure.
    /// </summary>
    private static void UpdateDryRunCardIdDiagnostics(ArchidektCopyResult result)
    {
        if (result.MissingArchidektCardIds <= 0)
        {
            return;
        }

        result.CardIdDiagnostics =
            $"{result.MissingArchidektCardIds} copied row(s) lack cached Archidekt card ids; "
            + "apply mode will resolve them before card writes.";
        result.NextAction ??= "Run archidekt_copy_workspace with dryRun=false after reviewing warnings.";
        result.Warnings.Add(
            "Missing cached Archidekt card ids in dry-run mean mtg-mcp will resolve those ids on apply; "
                + "they do not by themselves mean the copy will fail.");
    }

    /// <summary>
    /// Resolves all copied card ids before the first destination mutation.
    /// </summary>
    private async Task ResolveCopiedCardIdsBeforeMutationAsync(
        ArchidektCopyResult result,
        IReadOnlyList<DeckCard> copiedCards,
        CancellationToken cancellationToken)
    {
        if (copiedCards.Any(card => string.IsNullOrWhiteSpace(card.ArchidektCardId)))
        {
            await RequireArchidektGateway()
                .ResolveCardIdsAsync(copiedCards, cancellationToken)
                .ConfigureAwait(false);
        }

        UpdateCopyCardIdDiagnostics(result, copiedCards);
    }

    /// <summary>
    /// Marks a copy attempt as blocked before any Archidekt deck mutation happened.
    /// </summary>
    private static void BlockCopyBeforeMutation(
        ArchidektCopyResult result,
        string failedPhase,
        string reason)
    {
        result.CopyPhase = failedPhase;
        result.FailedPhase = failedPhase;
        result.VerificationStatus = "blocked";
        result.CanResume = false;
        result.NextAction = "Resolve the listed preflight issue, then run archidekt_copy_workspace again.";
        result.Warnings.Add(reason);
        result.RecoveryInstructions.Add("No destination card mutation was attempted.");
        result.RecoveryInstructions.Add(
            "Refresh card metadata or choose supported paper printings for unresolved rows before retrying.");
    }

    /// <summary>
    /// Estimates final destination row count for verification and recovery diagnostics.
    /// </summary>
    private static int EstimateExpectedCardRows(
        DeckWorkspace source,
        DeckWorkspace? destination,
        IReadOnlyList<DeckCard> copiedCards,
        bool createNew,
        bool replaceExistingDestination)
    {
        if (createNew || replaceExistingDestination)
        {
            return source.Cards.Count;
        }

        return (destination?.Cards.Count ?? 0) + copiedCards.Count;
    }

    /// <summary>
    /// Re-imports the destination and verifies that remote rows match the intended final rows.
    /// </summary>
    private async Task<bool> VerifyArchidektCopyAsync(
        DeckWorkspace destination,
        IReadOnlyList<DeckCard> expectedCards,
        ArchidektCopyResult result,
        CancellationToken cancellationToken)
    {
        result.CopyPhase = "verify";
        if (string.IsNullOrWhiteSpace(destination.ArchidektDeckId))
        {
            result.VerificationStatus = "failed";
            result.FailedPhase = "verify";
            result.RecoveryInstructions.Add("Verification could not run because the destination deck id was unavailable.");
            return false;
        }

        DeckWorkspace verified = await RequireArchidektGateway()
            .ImportDeckAsync(destination.ArchidektDeckId, writeBack: true, cancellationToken)
            .ConfigureAwait(false);
        result.DetectedCardRows = verified.Cards.Count;
        result.ExpectedCardRows = expectedCards.Count;
        if (HasSameCopiedCards(expectedCards, verified.Cards))
        {
            result.VerificationStatus = "verified";
            return true;
        }

        result.VerificationStatus = "mismatch";
        result.FailedPhase = "verify";
        result.CanResume = false;
        result.NextAction = string.IsNullOrWhiteSpace(result.CheckpointId)
            ? "Inspect the destination deck before retrying; final verification did not match the expected rows."
            : $"Restore Archidekt checkpoint {result.CheckpointId}, then retry the copy.";
        result.Warnings.Add(
            $"Archidekt verification mismatch: expected {result.ExpectedCardRows} row(s), "
                + $"detected {result.DetectedCardRows} row(s).");
        AddRestoreRecoveryInstruction(result);
        return false;
    }

    /// <summary>
    /// Converts non-cancel copy failures into explicit recovery diagnostics.
    /// </summary>
    private async Task PopulateCopyFailureAsync(
        ArchidektCopyResult result,
        DeckWorkspace? destination,
        bool createNew,
        bool replaceExistingDestination,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string failedPhase = string.IsNullOrWhiteSpace(result.CopyPhase)
            ? "unknown"
            : result.CopyPhase;
        result.FailedPhase = failedPhase;
        result.VerificationStatus = failedPhase.Equals("checkpoint", StringComparison.OrdinalIgnoreCase)
            || failedPhase.Equals("preflight", StringComparison.OrdinalIgnoreCase)
                ? "blocked"
                : "failed";
        result.Warnings.Add($"Archidekt copy stopped during {failedPhase}: {exception.Message}");

        if (destination is not null)
        {
            result.DestinationWorkspaceId = destination.Id;
            result.DestinationArchidektDeckId = destination.ArchidektDeckId;
            result.DestinationName = destination.Name;
        }

        await TryInspectFailedDestinationAsync(result, destination, cancellationToken)
            .ConfigureAwait(false);
        AddCopyFailureRecoveryInstructions(result, createNew, replaceExistingDestination);
    }

    /// <summary>
    /// Attempts to read the destination after a partial failure without hiding the original failure.
    /// </summary>
    private async Task TryInspectFailedDestinationAsync(
        ArchidektCopyResult result,
        DeckWorkspace? destination,
        CancellationToken cancellationToken)
    {
        if (destination is null || string.IsNullOrWhiteSpace(destination.ArchidektDeckId))
        {
            return;
        }

        try
        {
            DeckWorkspace inspected = await RequireArchidektGateway()
                .ImportDeckAsync(destination.ArchidektDeckId, writeBack: true, cancellationToken)
                .ConfigureAwait(false);
            result.DetectedCardRows = inspected.Cards.Count;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            result.Warnings.Add($"Could not inspect destination after failure: {exception.Message}");
        }
    }

    /// <summary>
    /// Adds phase-specific recovery guidance after a failed copy attempt.
    /// </summary>
    private static void AddCopyFailureRecoveryInstructions(
        ArchidektCopyResult result,
        bool createNew,
        bool replaceExistingDestination)
    {
        string failedPhase = result.FailedPhase ?? "unknown";
        if (failedPhase.Equals("checkpoint", StringComparison.OrdinalIgnoreCase))
        {
            result.CanResume = false;
            result.NextAction = "Fix checkpoint creation before retrying; no card replacement was attempted.";
            result.RecoveryInstructions.Add("No destination card mutation was attempted because checkpoint creation failed.");
            return;
        }

        if (createNew && failedPhase.Equals("add-cards", StringComparison.OrdinalIgnoreCase))
        {
            result.CanResume = true;
            result.ResumeDeckIdOrUrl = result.DestinationArchidektDeckId;
            result.NextAction =
                "Retry archidekt_copy_workspace with the same create-new inputs; mtg-mcp will reuse the migration marker and add missing rows.";
            result.RecoveryInstructions.Add("The destination was created by this migration, so retry can resume from copied row fingerprints.");
            return;
        }

        if (replaceExistingDestination
            && (failedPhase.Equals("remove-cards", StringComparison.OrdinalIgnoreCase)
                || failedPhase.Equals("add-cards", StringComparison.OrdinalIgnoreCase)
                || failedPhase.Equals("verify", StringComparison.OrdinalIgnoreCase)))
        {
            result.CanResume = false;
            result.NextAction = string.IsNullOrWhiteSpace(result.CheckpointId)
                ? "Inspect the destination deck before retrying; card replacement may be partial."
                : $"Restore Archidekt checkpoint {result.CheckpointId}, then retry replaceExistingDestination=true.";
            AddRestoreRecoveryInstruction(result);
            return;
        }

        result.CanResume = !string.IsNullOrWhiteSpace(result.DestinationArchidektDeckId);
        result.ResumeDeckIdOrUrl = result.CanResume ? result.DestinationArchidektDeckId : null;
        result.NextAction = result.CanResume
            ? "Retry the same archidekt_copy_workspace request after reviewing warnings."
            : "Retry after resolving the reported failure.";
        result.RecoveryInstructions.Add("No destructive replace phase was confirmed after the last safe boundary.");
    }

    /// <summary>
    /// Adds restore-first guidance when a replace attempt may have changed destination cards.
    /// </summary>
    private static void AddRestoreRecoveryInstruction(ArchidektCopyResult result)
    {
        if (string.IsNullOrWhiteSpace(result.CheckpointId))
        {
            result.RecoveryInstructions.Add("No checkpoint id was available; inspect the destination manually before retrying.");
            return;
        }

        result.RecoveryInstructions.Add($"Restore Archidekt checkpoint {result.CheckpointId} before retrying replacement.");
        result.RecoveryInstructions.Add("Do not rerun replace mode against the partial destination until the checkpoint is restored.");
    }

    /// <summary>
    /// Rejects ambiguous destination choices before any write.
    /// </summary>
    private static void ValidateDestinationChoice(
        bool createNew,
        string? destinationDeckIdOrUrl,
        bool allowNonEmptyDestination,
        bool replaceExistingDestination
    )
    {
        if (allowNonEmptyDestination && replaceExistingDestination)
        {
            throw new InvalidOperationException(
                "Choose either allowNonEmptyDestination=true to append cards "
                    + "or replaceExistingDestination=true to replace cards, not both."
            );
        }

        if (createNew && !string.IsNullOrWhiteSpace(destinationDeckIdOrUrl))
        {
            throw new InvalidOperationException(
                "Choose either createNew=true or destinationDeckIdOrUrl, not both."
            );
        }

        if (!createNew && string.IsNullOrWhiteSpace(destinationDeckIdOrUrl))
        {
            throw new InvalidOperationException(
                "Copying into an existing Archidekt deck requires destinationDeckIdOrUrl."
            );
        }
    }

    /// <summary>
    /// Checks whether a card's primary category contributes to the active deck.
    /// </summary>
    private static bool IsIncludedByPrimaryCategory(
        IReadOnlyDictionary<string, DeckCategory> categories,
        DeckCard card
    )
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        return !categories.TryGetValue(primaryCategory, out DeckCategory? category)
            || category.IncludedInDeck;
    }

    /// <summary>
    /// Adds warnings that depend on reading an existing destination deck.
    /// </summary>
    private static void AddDestinationWarnings(
        DeckWorkspace source,
        DeckWorkspace destination,
        ArchidektCopyResult result
    )
    {
        foreach (DeckSourceReference sourceReference in source.SourceReferences)
        {
            if (
                !string.IsNullOrWhiteSpace(destination.Description)
                && destination.Description.Contains(
                    $"{sourceReference.Provider}:{sourceReference.ExternalId}",
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                result.Warnings.Add(
                    $"Destination description already references {sourceReference.Provider}:{sourceReference.ExternalId}."
                );
            }
        }
    }

    /// <summary>
    /// Chooses destination description text without discarding source deck intent.
    /// </summary>
    private static string? SelectCopyDescription(
        string? explicitDescription,
        DeckWorkspace source,
        DeckWorkspace destination,
        bool createNew)
    {
        if (explicitDescription is not null)
        {
            return explicitDescription;
        }

        if (createNew)
        {
            return source.Description ?? destination.Description;
        }

        DeckIntentResult sourceIntent = DeckIntentText.Extract(source.Description, source.Id);
        if (sourceIntent.Found && !string.IsNullOrWhiteSpace(sourceIntent.IntentText))
        {
            return DeckIntentText.UpsertDescription(destination.Description, sourceIntent.IntentText);
        }

        return destination.Description;
    }

    /// <summary>
    /// Finds a previous create-new migration so retries do not create duplicate decks.
    /// </summary>
    private async Task<DeckWorkspace?> FindExistingMigrationDestinationAsync(
        DeckWorkspace source,
        string destinationName,
        CancellationToken cancellationToken
    )
    {
        string? marker = BuildMigrationMarker(source);
        if (string.IsNullOrWhiteSpace(marker))
        {
            return null;
        }

        IArchidektGateway archidekt = RequireArchidektGateway();
        IReadOnlyList<ArchidektDeckSummary> decks = await archidekt
            .ListDecksAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (ArchidektDeckSummary deck in decks)
        {
            if (
                string.IsNullOrWhiteSpace(deck.Id)
                || !destinationName.Equals(deck.Name, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            DeckWorkspace candidate = await archidekt
                .ImportDeckAsync(deck.Id, writeBack: true, cancellationToken)
                .ConfigureAwait(false);
            if (
                !string.IsNullOrWhiteSpace(candidate.Description)
                && candidate.Description.Contains(marker, StringComparison.OrdinalIgnoreCase)
            )
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether a previously created deck already contains this copied workspace.
    /// </summary>
    private static bool HasSameCopiedCards(
        IReadOnlyList<DeckCard> sourceCards,
        IReadOnlyList<DeckCard> destinationCards
    )
    {
        List<string> sourceFingerprints = BuildCardFingerprints(sourceCards);
        List<string> destinationFingerprints = BuildCardFingerprints(destinationCards);
        if (sourceFingerprints.Count != destinationFingerprints.Count)
        {
            return false;
        }

        sourceFingerprints.Sort(StringComparer.OrdinalIgnoreCase);
        destinationFingerprints.Sort(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < sourceFingerprints.Count; index++)
        {
            if (
                !sourceFingerprints[index].Equals(
                    destinationFingerprints[index],
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether the destination contains only rows that belong to the source copy.
    /// </summary>
    private static bool HasSubsetOfCopiedCards(
        IReadOnlyList<DeckCard> sourceCards,
        IReadOnlyList<DeckCard> destinationCards
    )
    {
        List<string> remainingSourceFingerprints = BuildCardFingerprints(sourceCards);
        foreach (string destinationFingerprint in BuildCardFingerprints(destinationCards))
        {
            int matchIndex = remainingSourceFingerprints.FindIndex(fingerprint =>
                fingerprint.Equals(destinationFingerprint, StringComparison.OrdinalIgnoreCase));
            if (matchIndex < 0)
            {
                return false;
            }

            remainingSourceFingerprints.RemoveAt(matchIndex);
        }

        return true;
    }

    /// <summary>
    /// Removes rows that already exist in the destination so a retry writes only the missing cards.
    /// </summary>
    private static List<DeckCard> FilterAlreadyCopiedCards(
        IReadOnlyList<DeckCard> copiedCards,
        IReadOnlyList<DeckCard> destinationCards
    )
    {
        List<string> destinationFingerprints = BuildCardFingerprints(destinationCards);
        List<DeckCard> missingCards = [];
        foreach (DeckCard copiedCard in copiedCards)
        {
            string fingerprint = BuildCardFingerprint(copiedCard);
            int matchIndex = destinationFingerprints.FindIndex(value =>
                value.Equals(fingerprint, StringComparison.OrdinalIgnoreCase));
            if (matchIndex >= 0)
            {
                destinationFingerprints.RemoveAt(matchIndex);
                continue;
            }

            missingCards.Add(copiedCard);
        }

        return missingCards;
    }

    /// <summary>
    /// Builds stable card fingerprints for migration retry comparisons.
    /// </summary>
    private static List<string> BuildCardFingerprints(IReadOnlyList<DeckCard> cards)
    {
        List<string> fingerprints = [];
        foreach (DeckCard card in cards)
        {
            fingerprints.Add(BuildCardFingerprint(card));
        }

        return fingerprints;
    }

    /// <summary>
    /// Builds a stable card fingerprint for migration retry comparisons.
    /// </summary>
    private static string BuildCardFingerprint(DeckCard card)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        List<string> categories = DeckCategoryOrdering.OrderedDistinct(
            primaryCategory,
            card.Categories);
        categories.Sort(StringComparer.OrdinalIgnoreCase);

        return string.Join(
            "|",
            card.Name,
            card.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            primaryCategory,
            card.ScryfallId ?? "",
            string.Join("\u001F", categories)
        );
    }

    /// <summary>
    /// Creates or updates Archidekt categories before card upload.
    /// </summary>
    private async Task CopyCategoriesToArchidektAsync(
        DeckWorkspace source,
        DeckWorkspace destination,
        CancellationToken cancellationToken
    )
    {
        foreach (DeckCategory sourceCategory in source.Categories)
        {
            DeckCategory? destinationCategory = destination.Categories.FirstOrDefault(category =>
                category.Name.Equals(sourceCategory.Name, StringComparison.OrdinalIgnoreCase)
            );
            if (destinationCategory is null)
            {
                destinationCategory = new DeckCategory
                {
                    Name = sourceCategory.Name,
                    IncludedInDeck = sourceCategory.IncludedInDeck,
                    IncludedInPrice = sourceCategory.IncludedInPrice,
                    IsPremier =
                        sourceCategory.IsPremier
                        || DeckDefaults.IsCommanderCategory(sourceCategory.Name),
                };
                destination.Categories.Add(destinationCategory);
            }
            else
            {
                destinationCategory.IncludedInDeck = sourceCategory.IncludedInDeck;
                destinationCategory.IncludedInPrice = sourceCategory.IncludedInPrice;
                destinationCategory.IsPremier =
                    sourceCategory.IsPremier
                    || DeckDefaults.IsCommanderCategory(sourceCategory.Name);
            }

            await PersistCategoryAsync(destination, destinationCategory, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Copies a workspace card while clearing destination-specific Archidekt relation state.
    /// </summary>
    private static DeckCard CloneForArchidektCopy(DeckCard source)
    {
        DeckCard copy = new()
        {
            Name = source.Name,
            Quantity = source.Quantity,
            PrimaryCategory = DeckCategoryOrdering.PrimaryCategory(source),
            Categories = DeckCategoryOrdering.OrderedDistinct(
                DeckCategoryOrdering.PrimaryCategory(source),
                source.Categories),
            ScryfallId = source.ScryfallId,
            ScryfallOracleId = source.ScryfallOracleId,
            ArchidektCardId = source.ArchidektCardId,
            Modifier = source.Modifier,
            Companion = source.Companion,
            FlippedDefault = source.FlippedDefault,
            Snapshot = CloneSnapshot(source.Snapshot),
            Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.OrdinalIgnoreCase),
        };

        copy.ArchidektDeckRelationId = null;
        return copy;
    }

    /// <summary>
    /// Reuses known Archidekt print ids before replacing destination card rows.
    /// </summary>
    private static void CopyKnownArchidektCardIds(
        IReadOnlyList<DeckCard> destinationCards,
        IReadOnlyList<DeckCard> copiedCards
    )
    {
        Dictionary<string, Queue<DeckCard>> byPrint = BuildDestinationCardIdLookup(
            destinationCards,
            includePrint: true);
        Dictionary<string, Queue<DeckCard>> byNameAndCategory = BuildDestinationCardIdLookup(
            destinationCards,
            includePrint: false);

        foreach (DeckCard copiedCard in copiedCards)
        {
            if (!string.IsNullOrWhiteSpace(copiedCard.ArchidektCardId))
            {
                continue;
            }

            DeckCard? matched = DequeueMatch(byPrint, BuildCardIdReuseKey(copiedCard, includePrint: true))
                ?? DequeueMatch(byNameAndCategory, BuildCardIdReuseKey(copiedCard, includePrint: false));
            if (!string.IsNullOrWhiteSpace(matched?.ArchidektCardId))
            {
                copiedCard.ArchidektCardId = matched.ArchidektCardId;
            }
        }
    }

    /// <summary>
    /// Groups destination cards by identity fields useful for reusing Archidekt print ids.
    /// </summary>
    private static Dictionary<string, Queue<DeckCard>> BuildDestinationCardIdLookup(
        IEnumerable<DeckCard> destinationCards,
        bool includePrint
    )
    {
        Dictionary<string, Queue<DeckCard>> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in destinationCards.Where(card => !string.IsNullOrWhiteSpace(card.ArchidektCardId)))
        {
            string? key = BuildCardIdReuseKey(card, includePrint);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!lookup.TryGetValue(key, out Queue<DeckCard>? queue))
            {
                queue = new Queue<DeckCard>();
                lookup[key] = queue;
            }

            queue.Enqueue(card);
        }

        return lookup;
    }

    /// <summary>
    /// Creates a stable card-id reuse key from source-visible card identity.
    /// </summary>
    private static string? BuildCardIdReuseKey(DeckCard card, bool includePrint)
    {
        string primary = DeckCategoryOrdering.PrimaryCategory(card);
        if (!includePrint)
        {
            return $"{card.Name}|{primary}";
        }

        string? print = string.IsNullOrWhiteSpace(card.ScryfallId)
            ? null
            : card.ScryfallId;
        if (string.IsNullOrWhiteSpace(print)
            && !string.IsNullOrWhiteSpace(card.Snapshot.Set)
            && !string.IsNullOrWhiteSpace(card.Snapshot.CollectorNumber))
        {
            print = $"{card.Snapshot.Set}|{card.Snapshot.CollectorNumber}";
        }

        if (string.IsNullOrWhiteSpace(print))
        {
            return null;
        }

        return $"{card.Name}|{primary}|{print}";
    }

    /// <summary>
    /// Removes one matched destination card from a reuse lookup.
    /// </summary>
    private static DeckCard? DequeueMatch(
        Dictionary<string, Queue<DeckCard>> lookup,
        string? key
    )
    {
        if (string.IsNullOrWhiteSpace(key)
            || !lookup.TryGetValue(key, out Queue<DeckCard>? matches)
            || matches.Count == 0)
        {
            return null;
        }

        return matches.Dequeue();
    }

    /// <summary>
    /// Copies cached card facts without sharing mutable collection instances.
    /// </summary>
    private static CardSnapshot CloneSnapshot(CardSnapshot snapshot)
    {
        return DeckServiceHelpers.CopyCardSnapshot(snapshot);
    }

    /// <summary>
    /// Appends a small provenance marker to the destination description for repeat-copy warnings.
    /// </summary>
    private static string? BuildMigrationDescription(string? description, DeckWorkspace source)
    {
        string? marker = BuildMigrationMarker(source);
        if (marker is null)
        {
            return description;
        }

        if (!string.IsNullOrWhiteSpace(description)
            && description.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            return description;
        }

        return string.IsNullOrWhiteSpace(description)
            ? marker
            : $"{description.Trim()}\n\n{marker}";
    }

    /// <summary>
    /// Builds the provenance marker used to recognize repeat migration attempts.
    /// </summary>
    private static string? BuildMigrationMarker(DeckWorkspace source)
    {
        if (source.SourceReferences.Count == 0)
        {
            return null;
        }

        string sourceText = string.Join(
            ", ",
            source.SourceReferences.Select(reference => $"{reference.Provider}:{reference.ExternalId}")
        );
        return $"MTG MCP Migration Source: {sourceText}; Workspace: {source.Id}";
    }
}
