namespace MtgMcp.Core;

/// <summary>
/// Clones deck edit plans after validating target workspace compatibility.
/// </summary>
public sealed partial class DeckPlanService
{
    /// <summary>
    /// Creates a new draft plan for another workspace after source, commander, format, category, and card identity checks.
    /// </summary>
    public async Task<DeckEditPlan> CloneDeckPlanAsync(
        string planId,
        string targetWorkspaceId,
        CancellationToken cancellationToken)
    {
        DeckEditPlan sourcePlan = await GetDeckPlanAsync(planId, cancellationToken)
            .ConfigureAwait(false);
        DeckWorkspace sourceWorkspace = await LoadWorkspaceAsync(sourcePlan.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckWorkspace targetWorkspace = await LoadWorkspaceAsync(targetWorkspaceId, cancellationToken)
            .ConfigureAwait(false);

        ValidatePlanCloneCompatibility(sourcePlan, sourceWorkspace, targetWorkspace);
        DeckEditPlan clone = new()
        {
            WorkspaceId = targetWorkspace.Id,
            Name = string.IsNullOrWhiteSpace(sourcePlan.Name)
                ? "Cloned deck edit plan"
                : $"{sourcePlan.Name} (clone)",
            Kind = sourcePlan.Kind,
            Status = DeckEditPlanStatus.Draft,
            Persistence = DeckPersistence.For(targetWorkspace),
            Rationale = string.IsNullOrWhiteSpace(sourcePlan.Rationale)
                ? $"Cloned from plan {sourcePlan.PlanId}."
                : $"{sourcePlan.Rationale}\nCloned from plan {sourcePlan.PlanId}.",
            Confidence = sourcePlan.Confidence,
            Operations = sourcePlan.Operations.Select(CloneOperation).ToList(),
            Warnings = sourcePlan.Warnings.ToList()
        };
        clone.Warnings.Add($"Cloned from workspace {sourceWorkspace.Id}; validate preview before applying.");
        return await RequirePlanRepository().SaveAsync(clone, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates that the target workspace is a safe destination for cloned operations.
    /// </summary>
    private static void ValidatePlanCloneCompatibility(
        DeckEditPlan plan,
        DeckWorkspace sourceWorkspace,
        DeckWorkspace targetWorkspace)
    {
        if (!sourceWorkspace.Format.Equals(targetWorkspace.Format, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot clone deck plan because source and target workspace formats differ.");
        }

        string sourceReference = BuildCloneSourceSignature(sourceWorkspace);
        string targetReference = BuildCloneSourceSignature(targetWorkspace);
        if (!sourceReference.Equals(targetReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cannot clone deck plan because source references differ. Reopen or import both workspaces from the same deck first.");
        }

        ValidateCommanderIdentity(sourceWorkspace, targetWorkspace);
        ValidateOperationCategories(plan, targetWorkspace);
        ValidateOperationCardIdentities(plan, sourceWorkspace, targetWorkspace);
    }

    /// <summary>
    /// Builds a stable external source signature for clone validation.
    /// </summary>
    private static string BuildCloneSourceSignature(DeckWorkspace workspace)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
        {
            parts.Add($"{DeckImportProviders.Archidekt}:{workspace.ArchidektDeckId}");
        }

        foreach (DeckSourceReference reference in workspace.SourceReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.Provider) && !string.IsNullOrWhiteSpace(reference.ExternalId))
            {
                parts.Add($"{reference.Provider}:{reference.ExternalId}");
            }
        }

        parts.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("|", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validates that commander card identities match.
    /// </summary>
    private static void ValidateCommanderIdentity(DeckWorkspace sourceWorkspace, DeckWorkspace targetWorkspace)
    {
        List<string> sourceCommanders = CommanderIdentities(sourceWorkspace);
        List<string> targetCommanders = CommanderIdentities(targetWorkspace);
        if (!SameValues(sourceCommanders, targetCommanders))
        {
            throw new InvalidOperationException("Cannot clone deck plan because commander identities differ.");
        }
    }

    /// <summary>
    /// Gets commander identities for clone compatibility checks.
    /// </summary>
    private static List<string> CommanderIdentities(DeckWorkspace workspace)
    {
        List<string> identities = [];
        foreach (DeckCard card in workspace.Cards)
        {
            if (IsCommanderCard(card))
            {
                identities.Add(CloneCardIdentity(card));
            }
        }

        identities.Sort(StringComparer.OrdinalIgnoreCase);
        return identities;
    }

    /// <summary>
    /// Validates that categories named by plan operations exist on the target workspace.
    /// </summary>
    private static void ValidateOperationCategories(DeckEditPlan plan, DeckWorkspace targetWorkspace)
    {
        HashSet<string> categories = targetWorkspace.Categories
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (DeckEditOperation operation in plan.Operations)
        {
            foreach (string category in OperationCategories(operation))
            {
                if (!categories.Contains(category))
                {
                    throw new InvalidOperationException(
                        $"Cannot clone deck plan because target workspace lacks category '{category}'."
                    );
                }
            }
        }
    }

    /// <summary>
    /// Identifies the source and destination categories that make a cloned plan valid.
    /// </summary>
    private static IEnumerable<string> OperationCategories(DeckEditOperation operation)
    {
        if (!string.IsNullOrWhiteSpace(operation.Category))
        {
            yield return operation.Category;
        }

        if (!string.IsNullOrWhiteSpace(operation.FromCategory))
        {
            yield return operation.FromCategory;
        }

        if (!string.IsNullOrWhiteSpace(operation.ToCategory))
        {
            yield return operation.ToCategory;
        }
    }

    /// <summary>
    /// Validates existing-card operations against the target workspace.
    /// </summary>
    private static void ValidateOperationCardIdentities(
        DeckEditPlan plan,
        DeckWorkspace sourceWorkspace,
        DeckWorkspace targetWorkspace)
    {
        foreach (DeckEditOperation operation in plan.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.CardName)
                || operation.Operation.Equals(DeckEditOperations.AddCard, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DeckCard sourceCard = FindCard(sourceWorkspace, operation.CardName, OperationLookupCategory(operation))
                ?? throw new InvalidOperationException(
                    $"Cannot clone deck plan because source workspace no longer contains '{operation.CardName}'.");
            DeckCard targetCard = FindCard(targetWorkspace, operation.CardName, OperationLookupCategory(operation))
                ?? throw new InvalidOperationException(
                    $"Cannot clone deck plan because target workspace does not contain '{operation.CardName}'.");
            if (!CloneCardIdentity(sourceCard).Equals(CloneCardIdentity(targetCard), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cannot clone deck plan because card identity differs for '{operation.CardName}'.");
            }
        }
    }

    /// <summary>
    /// Chooses the category used to locate a card for clone identity validation.
    /// </summary>
    private static string? OperationLookupCategory(DeckEditOperation operation)
    {
        return !string.IsNullOrWhiteSpace(operation.FromCategory)
            ? operation.FromCategory
            : operation.Category;
    }

    /// <summary>
    /// Builds a stable card identity for clone validation.
    /// </summary>
    private static string CloneCardIdentity(DeckCard card)
    {
        if (!string.IsNullOrWhiteSpace(card.ScryfallOracleId))
        {
            return $"oracle:{card.ScryfallOracleId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(card.ScryfallId))
        {
            return $"scryfall:{card.ScryfallId.Trim()}";
        }

        return $"name:{card.Name.Trim()}";
    }

    /// <summary>
    /// Checks whether two sorted string lists contain the same values ignoring case.
    /// </summary>
    private static bool SameValues(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count
            && left.All(value => right.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Copies one plan step so later edits to either draft cannot share mutable state.
    /// </summary>
    private static DeckEditOperation CloneOperation(DeckEditOperation operation)
    {
        return new DeckEditOperation
        {
            Operation = operation.Operation,
            CardName = operation.CardName,
            ReplacementCardName = operation.ReplacementCardName,
            Quantity = operation.Quantity,
            Category = operation.Category,
            FromCategory = operation.FromCategory,
            ToCategory = operation.ToCategory,
            Name = operation.Name,
            Format = operation.Format,
            Description = operation.Description,
            IncludedInDeck = operation.IncludedInDeck,
            IncludedInPrice = operation.IncludedInPrice,
            Rationale = operation.Rationale
        };
    }
}
