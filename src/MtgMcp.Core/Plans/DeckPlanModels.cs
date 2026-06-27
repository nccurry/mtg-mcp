using System.Text.Json.Serialization;

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
    public DeckEditPlanStatus Status { get; set; } = DeckEditPlanStatus.Draft;

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
/// Represents the closed set of deck edit steps that can be stored in a plan.
/// </summary>
[JsonConverter(typeof(DeckEditOperationJsonConverter))]
public readonly union DeckEditOperation(
    DeckEditOperation.AddCardOperation,
    DeckEditOperation.RemoveCardOperation,
    DeckEditOperation.SetCardQuantityOperation,
    DeckEditOperation.MoveCardOperation,
    DeckEditOperation.AddCardCategoryOperation,
    DeckEditOperation.RemoveCardCategoryOperation,
    DeckEditOperation.SetPrimaryCardCategoryOperation,
    DeckEditOperation.CreateCategoryOperation,
    DeckEditOperation.RenameCategoryOperation,
    DeckEditOperation.DeleteCategoryOperation,
    DeckEditOperation.UpdateDeckMetadataOperation
)
{
    /// <summary>
    /// Gets the stable serialized operation token.
    /// </summary>
    public string Operation => this switch
    {
        AddCardOperation => DeckEditOperations.AddCard,
        RemoveCardOperation => DeckEditOperations.RemoveCard,
        SetCardQuantityOperation => DeckEditOperations.SetCardQuantity,
        MoveCardOperation => DeckEditOperations.MoveCard,
        AddCardCategoryOperation => DeckEditOperations.AddCardCategory,
        RemoveCardCategoryOperation => DeckEditOperations.RemoveCardCategory,
        SetPrimaryCardCategoryOperation => DeckEditOperations.SetPrimaryCardCategory,
        CreateCategoryOperation => DeckEditOperations.CreateCategory,
        RenameCategoryOperation => DeckEditOperations.RenameCategory,
        DeleteCategoryOperation => DeckEditOperations.DeleteCategory,
        UpdateDeckMetadataOperation => DeckEditOperations.UpdateDeckMetadata
    };

    /// <summary>
    /// Gets the card name for card-targeted operations.
    /// </summary>
    public string? CardName => this switch
    {
        AddCardOperation operation => operation.CardName,
        RemoveCardOperation operation => operation.CardName,
        SetCardQuantityOperation operation => operation.CardName,
        MoveCardOperation operation => operation.CardName,
        AddCardCategoryOperation operation => operation.CardName,
        RemoveCardCategoryOperation operation => operation.CardName,
        SetPrimaryCardCategoryOperation operation => operation.CardName,
        CreateCategoryOperation => null,
        RenameCategoryOperation => null,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation => null
    };

    /// <summary>
    /// Gets the optional replacement card name retained for legacy flat JSON compatibility.
    /// </summary>
    public string? ReplacementCardName => null;

    /// <summary>
    /// Gets the quantity used by card add, remove, and set-quantity operations.
    /// </summary>
    public int? Quantity => this switch
    {
        AddCardOperation operation => operation.Quantity,
        RemoveCardOperation operation => operation.Quantity,
        SetCardQuantityOperation operation => operation.Quantity,
        MoveCardOperation => null,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation => null,
        RenameCategoryOperation => null,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation => null
    };

    /// <summary>
    /// Gets the category used by single-category operations.
    /// </summary>
    public string? Category => this switch
    {
        AddCardOperation operation => operation.Category,
        RemoveCardOperation operation => operation.Category,
        SetCardQuantityOperation operation => operation.Category,
        MoveCardOperation => null,
        AddCardCategoryOperation operation => operation.Category,
        RemoveCardCategoryOperation operation => operation.Category,
        SetPrimaryCardCategoryOperation operation => operation.Category,
        CreateCategoryOperation operation => operation.Category,
        RenameCategoryOperation => null,
        DeleteCategoryOperation operation => operation.Category,
        UpdateDeckMetadataOperation => null
    };

    /// <summary>
    /// Gets the source category used by move and rename operations.
    /// </summary>
    public string? FromCategory => this switch
    {
        AddCardOperation => null,
        RemoveCardOperation => null,
        SetCardQuantityOperation => null,
        MoveCardOperation operation => operation.FromCategory,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation => null,
        RenameCategoryOperation operation => operation.FromCategory,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation => null
    };

    /// <summary>
    /// Gets the destination category used by move, rename, and delete operations.
    /// </summary>
    public string? ToCategory => this switch
    {
        AddCardOperation => null,
        RemoveCardOperation => null,
        SetCardQuantityOperation => null,
        MoveCardOperation operation => operation.ToCategory,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation => null,
        RenameCategoryOperation operation => operation.ToCategory,
        DeleteCategoryOperation operation => operation.ToCategory,
        UpdateDeckMetadataOperation => null
    };

    /// <summary>
    /// Gets the replacement deck name for metadata updates.
    /// </summary>
    public string? Name => this switch
    {
        AddCardOperation => null,
        RemoveCardOperation => null,
        SetCardQuantityOperation => null,
        MoveCardOperation => null,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation => null,
        RenameCategoryOperation => null,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation operation => operation.Name
    };

    /// <summary>
    /// Gets the replacement deck format for metadata updates.
    /// </summary>
    public string? Format => this switch
    {
        AddCardOperation => null,
        RemoveCardOperation => null,
        SetCardQuantityOperation => null,
        MoveCardOperation => null,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation => null,
        RenameCategoryOperation => null,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation operation => operation.Format
    };

    /// <summary>
    /// Gets the replacement deck description for metadata updates.
    /// </summary>
    public string? Description => this switch
    {
        AddCardOperation => null,
        RemoveCardOperation => null,
        SetCardQuantityOperation => null,
        MoveCardOperation => null,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation => null,
        RenameCategoryOperation => null,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation operation => operation.Description
    };

    /// <summary>
    /// Gets whether a created category contributes cards to deck legality and count totals.
    /// </summary>
    public bool? IncludedInDeck => this switch
    {
        AddCardOperation => null,
        RemoveCardOperation => null,
        SetCardQuantityOperation => null,
        MoveCardOperation => null,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation operation => operation.IncludedInDeck,
        RenameCategoryOperation => null,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation => null
    };

    /// <summary>
    /// Gets whether a created category contributes cards to price totals.
    /// </summary>
    public bool? IncludedInPrice => this switch
    {
        AddCardOperation => null,
        RemoveCardOperation => null,
        SetCardQuantityOperation => null,
        MoveCardOperation => null,
        AddCardCategoryOperation => null,
        RemoveCardCategoryOperation => null,
        SetPrimaryCardCategoryOperation => null,
        CreateCategoryOperation operation => operation.IncludedInPrice,
        RenameCategoryOperation => null,
        DeleteCategoryOperation => null,
        UpdateDeckMetadataOperation => null
    };

    /// <summary>
    /// Gets the human rationale for the edit step.
    /// </summary>
    public string Rationale => this switch
    {
        AddCardOperation operation => operation.Rationale,
        RemoveCardOperation operation => operation.Rationale,
        SetCardQuantityOperation operation => operation.Rationale,
        MoveCardOperation operation => operation.Rationale,
        AddCardCategoryOperation operation => operation.Rationale,
        RemoveCardCategoryOperation operation => operation.Rationale,
        SetPrimaryCardCategoryOperation operation => operation.Rationale,
        CreateCategoryOperation operation => operation.Rationale,
        RenameCategoryOperation operation => operation.Rationale,
        DeleteCategoryOperation operation => operation.Rationale,
        UpdateDeckMetadataOperation operation => operation.Rationale
    };

    /// <summary>
    /// Gets whether this operation can be combined into the optimized card-mutation batch.
    /// </summary>
    public bool IsCardBatchOperation => this switch
    {
        AddCardOperation => true,
        RemoveCardOperation => true,
        SetCardQuantityOperation => true,
        MoveCardOperation => true,
        AddCardCategoryOperation => true,
        RemoveCardCategoryOperation => true,
        SetPrimaryCardCategoryOperation => true,
        CreateCategoryOperation => false,
        RenameCategoryOperation => false,
        DeleteCategoryOperation => false,
        UpdateDeckMetadataOperation => false
    };

    /// <summary>
    /// Gets whether this operation can increase the included Commander card count.
    /// </summary>
    public bool CanIncreaseCommanderIncludedCount => this switch
    {
        AddCardOperation => true,
        RemoveCardOperation => false,
        SetCardQuantityOperation => true,
        MoveCardOperation => true,
        AddCardCategoryOperation => false,
        RemoveCardCategoryOperation => false,
        SetPrimaryCardCategoryOperation => true,
        CreateCategoryOperation => false,
        RenameCategoryOperation => false,
        DeleteCategoryOperation => false,
        UpdateDeckMetadataOperation => false
    };

    /// <summary>
    /// Returns this immutable operation for call sites that previously cloned mutable DTOs.
    /// </summary>
    public DeckEditOperation Clone()
    {
        return this;
    }

    /// <summary>
    /// Builds a typed edit that adds a card to the deck or a category.
    /// </summary>
    public static DeckEditOperation AddCard(
        string cardName,
        int? quantity = null,
        string? category = null,
        string rationale = "")
    {
        return new AddCardOperation(cardName, quantity, category, rationale);
    }

    /// <summary>
    /// Builds a typed edit that removes one or more copies of a card.
    /// </summary>
    public static DeckEditOperation RemoveCard(
        string cardName,
        int? quantity = null,
        string? category = null,
        string rationale = "")
    {
        return new RemoveCardOperation(cardName, quantity, category, rationale);
    }

    /// <summary>
    /// Builds a typed edit that changes a card count to an explicit quantity.
    /// </summary>
    public static DeckEditOperation SetCardQuantity(
        string cardName,
        int? quantity = null,
        string? category = null,
        string rationale = "")
    {
        return new SetCardQuantityOperation(cardName, quantity, category, rationale);
    }

    /// <summary>
    /// Builds a typed edit that moves a card between categories.
    /// </summary>
    public static DeckEditOperation MoveCard(
        string cardName,
        string? fromCategory,
        string toCategory,
        string rationale = "")
    {
        return new MoveCardOperation(cardName, fromCategory, toCategory, rationale);
    }

    /// <summary>
    /// Builds a typed edit that assigns an additional category to a card.
    /// </summary>
    public static DeckEditOperation AddCardCategory(
        string cardName,
        string category,
        string rationale = "")
    {
        return new AddCardCategoryOperation(cardName, category, rationale);
    }

    /// <summary>
    /// Builds a typed edit that removes a category assignment from a card.
    /// </summary>
    public static DeckEditOperation RemoveCardCategory(
        string cardName,
        string category,
        string rationale = "")
    {
        return new RemoveCardCategoryOperation(cardName, category, rationale);
    }

    /// <summary>
    /// Builds a typed edit that makes a category the card's primary grouping.
    /// </summary>
    public static DeckEditOperation SetPrimaryCardCategory(
        string cardName,
        string category,
        string rationale = "")
    {
        return new SetPrimaryCardCategoryOperation(cardName, category, rationale);
    }

    /// <summary>
    /// Builds a typed edit that defines a deck category and its inclusion flags.
    /// </summary>
    public static DeckEditOperation CreateCategory(
        string category,
        bool? includedInDeck = null,
        bool? includedInPrice = null,
        string rationale = "")
    {
        return new CreateCategoryOperation(category, includedInDeck, includedInPrice, rationale);
    }

    /// <summary>
    /// Builds a typed edit that renames one deck category across the workspace.
    /// </summary>
    public static DeckEditOperation RenameCategory(
        string fromCategory,
        string toCategory,
        string rationale = "")
    {
        return new RenameCategoryOperation(fromCategory, toCategory, rationale);
    }

    /// <summary>
    /// Builds a typed edit that deletes a category and optionally moves its cards.
    /// </summary>
    public static DeckEditOperation DeleteCategory(
        string category,
        string? toCategory = null,
        string rationale = "")
    {
        return new DeleteCategoryOperation(category, toCategory, rationale);
    }

    /// <summary>
    /// Builds a typed edit that changes deck-level metadata.
    /// </summary>
    public static DeckEditOperation UpdateDeckMetadata(
        string? name = null,
        string? format = null,
        string? description = null,
        string rationale = "")
    {
        return new UpdateDeckMetadataOperation(name, format, description, rationale);
    }

    /// <summary>
    /// Adds card copies to a category.
    /// </summary>
    public sealed record AddCardOperation(
        string CardName,
        int? Quantity,
        string? Category,
        string Rationale);

    /// <summary>
    /// Removes card copies from a category or from the first matching card.
    /// </summary>
    public sealed record RemoveCardOperation(
        string CardName,
        int? Quantity,
        string? Category,
        string Rationale);

    /// <summary>
    /// Sets the quantity of a card in a category or on the first matching card.
    /// </summary>
    public sealed record SetCardQuantityOperation(
        string CardName,
        int? Quantity,
        string? Category,
        string Rationale);

    /// <summary>
    /// Moves a card from one category to another primary category.
    /// </summary>
    public sealed record MoveCardOperation(
        string CardName,
        string? FromCategory,
        string ToCategory,
        string Rationale);

    /// <summary>
    /// Adds a secondary category to a card.
    /// </summary>
    public sealed record AddCardCategoryOperation(
        string CardName,
        string Category,
        string Rationale);

    /// <summary>
    /// Removes a category from a card.
    /// </summary>
    public sealed record RemoveCardCategoryOperation(
        string CardName,
        string Category,
        string Rationale);

    /// <summary>
    /// Makes a card category the primary category.
    /// </summary>
    public sealed record SetPrimaryCardCategoryOperation(
        string CardName,
        string Category,
        string Rationale);

    /// <summary>
    /// Creates a deck category and its inclusion flags.
    /// </summary>
    public sealed record CreateCategoryOperation(
        string Category,
        bool? IncludedInDeck,
        bool? IncludedInPrice,
        string Rationale);

    /// <summary>
    /// Renames a deck category.
    /// </summary>
    public sealed record RenameCategoryOperation(
        string FromCategory,
        string ToCategory,
        string Rationale);

    /// <summary>
    /// Deletes a deck category and moves cards to a fallback category.
    /// </summary>
    public sealed record DeleteCategoryOperation(
        string Category,
        string? ToCategory,
        string Rationale);

    /// <summary>
    /// Updates deck metadata fields.
    /// </summary>
    public sealed record UpdateDeckMetadataOperation(
        string? Name,
        string? Format,
        string? Description,
        string Rationale);
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
    public DeckEditPlanStatus Status { get; set; } = DeckEditPlanStatus.Applied;

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
/// Lists the closed set of deck edit plan apply states.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeckEditPlanStatus>))]
public enum DeckEditPlanStatus
{
    /// <summary>
    /// The plan is saved but has not been applied.
    /// </summary>
    [JsonStringEnumMemberName("draft")]
    Draft,

    /// <summary>
    /// Every operation completed and the plan was saved as applied.
    /// </summary>
    [JsonStringEnumMemberName("applied")]
    Applied,

    /// <summary>
    /// No operation completed and the plan was saved as failed.
    /// </summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>
    /// At least one operation completed before a later failure.
    /// </summary>
    [JsonStringEnumMemberName("partially-applied")]
    PartiallyApplied,

    /// <summary>
    /// A remote write may have succeeded but the client did not receive confirmation.
    /// </summary>
    [JsonStringEnumMemberName("apply-state-unknown")]
    ApplyStateUnknown,
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
