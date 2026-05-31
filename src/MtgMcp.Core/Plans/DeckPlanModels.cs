namespace MtgMcp.Core;

/// <summary>
/// Provides deck edit plan behavior.
/// </summary>
public sealed class DeckEditPlan
{
    /// <summary>
    /// Gets or sets the plan id.
    /// </summary>
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public string Status { get; set; } = DeckEditPlanStatus.Draft;

    /// <summary>
    /// Gets or sets the persistence.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets the created at.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the applied at.
    /// </summary>
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>
    /// Gets or sets the checkpoint id.
    /// </summary>
    public string? CheckpointId { get; set; }

    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets the confidence.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets the warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets the operations.
    /// </summary>
    public List<DeckEditOperation> Operations { get; set; } = [];
}

/// <summary>
/// Provides deck edit operation behavior.
/// </summary>
public sealed class DeckEditOperation
{
    /// <summary>
    /// Gets or sets the edit operation name.
    /// </summary>
    public string Operation { get; set; } = "";

    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string? CardName { get; set; }

    /// <summary>
    /// Gets or sets the replacement card name.
    /// </summary>
    public string? ReplacementCardName { get; set; }

    /// <summary>
    /// Gets or sets the quantity.
    /// </summary>
    public int? Quantity { get; set; }

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the from category.
    /// </summary>
    public string? FromCategory { get; set; }

    /// <summary>
    /// Gets or sets the to category.
    /// </summary>
    public string? ToCategory { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the format.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the included in deck.
    /// </summary>
    public bool? IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets the included in price.
    /// </summary>
    public bool? IncludedInPrice { get; set; }

    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Describes one caller-selected card add or remove for an explicit deck edit plan.
/// </summary>
public sealed class ExplicitDeckPlanCardChange
{
    /// <summary>
    /// Gets or sets the exact card name supplied by the caller.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the quantity to add or remove; values below one are treated as one.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Identifies the workspace category the caller wants this change to target.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Captures the caller's reason for choosing this card change.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Provides deck edit plan apply result behavior.
/// </summary>
public sealed class DeckEditPlanApplyResult
{
    /// <summary>
    /// Gets or sets whether every plan operation completed.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets the plan id.
    /// </summary>
    public string PlanId { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the persistence.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets the checkpoint id.
    /// </summary>
    public string? CheckpointId { get; set; }

    /// <summary>
    /// Gets or sets the saved status of the plan after the apply attempt.
    /// </summary>
    public string Status { get; set; } = DeckEditPlanStatus.Applied;

    /// <summary>
    /// Gets or sets the applied operations.
    /// </summary>
    public int AppliedOperations { get; set; }

    /// <summary>
    /// Gets or sets the operations that were attempted before success or failure was known.
    /// </summary>
    public int AttemptedOperations { get; set; }

    /// <summary>
    /// Gets or sets the zero-based operation index that failed, when known.
    /// </summary>
    public int? FailedOperationIndex { get; set; }

    /// <summary>
    /// Gets or sets the concrete failed edit step, when the failure can be tied to a single step.
    /// </summary>
    public DeckEditOperation? FailedOperation { get; set; }

    /// <summary>
    /// Gets or sets a sanitized failure summary for MCP clients.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the messages.
    /// </summary>
    public List<string> Messages { get; set; } = [];

    /// <summary>
    /// Gets or sets the workspace.
    /// </summary>
    public DeckWorkspace Workspace { get; set; } = new();
}

/// <summary>
/// Reports whether a deck edit plan deletion found a saved plan.
/// </summary>
public sealed class DeckEditPlanDeleteResult
{
    /// <summary>
    /// Gets or sets the requested plan id.
    /// </summary>
    public string PlanId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether a plan file was deleted.
    /// </summary>
    public bool Deleted { get; set; }
}

/// <summary>
/// Provides deck edit plan statuses.
/// </summary>
public static class DeckEditPlanStatus
{
    /// <summary>
    /// Stores the draft status.
    /// </summary>
    public const string Draft = "draft";

    /// <summary>
    /// Stores the applied status.
    /// </summary>
    public const string Applied = "applied";

    /// <summary>
    /// Stores the failed status.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// Stores the partially applied status.
    /// </summary>
    public const string PartiallyApplied = "partially-applied";

    /// <summary>
    /// Stores the status used when a remote write may have succeeded but the client did not receive confirmation.
    /// </summary>
    public const string ApplyStateUnknown = "apply-state-unknown";
}

/// <summary>
/// Provides deck edit operation names.
/// </summary>
public static class DeckEditOperations
{
    /// <summary>
    /// Stores the add card edit name.
    /// </summary>
    public const string AddCard = "deck_add_card";

    /// <summary>
    /// Stores the remove card edit name.
    /// </summary>
    public const string RemoveCard = "deck_remove_card";

    /// <summary>
    /// Stores the set card quantity edit name.
    /// </summary>
    public const string SetCardQuantity = "deck_set_card_quantity";

    /// <summary>
    /// Stores the move card edit name.
    /// </summary>
    public const string MoveCard = "deck_move_card";

    /// <summary>
    /// Stores the add card category edit name.
    /// </summary>
    public const string AddCardCategory = "deck_add_card_category";

    /// <summary>
    /// Stores the remove card category edit name.
    /// </summary>
    public const string RemoveCardCategory = "deck_remove_card_category";

    /// <summary>
    /// Stores the set primary card category edit name.
    /// </summary>
    public const string SetPrimaryCardCategory = "deck_set_primary_card_category";

    /// <summary>
    /// Stores the create category edit name.
    /// </summary>
    public const string CreateCategory = "deck_create_category";

    /// <summary>
    /// Stores the rename category edit name.
    /// </summary>
    public const string RenameCategory = "deck_rename_category";

    /// <summary>
    /// Stores the delete category edit name.
    /// </summary>
    public const string DeleteCategory = "deck_delete_category";

    /// <summary>
    /// Stores the update deck metadata edit name.
    /// </summary>
    public const string UpdateDeckMetadata = "deck_update_metadata";
}
