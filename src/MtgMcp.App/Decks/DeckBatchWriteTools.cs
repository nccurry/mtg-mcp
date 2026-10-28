using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Exposes the closed batch mutation with a complete discriminator schema.
/// </summary>
internal sealed class DeckBatchWriteTools
{
    /// <summary>
    /// Identifies every allowed batch discriminator for schema enrichment.
    /// </summary>
    private static readonly HashSet<string> ChangeKinds = new(StringComparer.Ordinal)
    {
        "update-metadata",
        "add-entry",
        "update-entry",
        "remove-entry",
        "add-category",
        "update-category",
        "remove-category",
        "assign-category",
        "unassign-category",
        "upsert-provider-binding",
        "remove-provider-binding",
    };

    /// <summary>
    /// Stores the local deck transaction boundary.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Stores the effective process authority for defense in depth.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates the batch tool around one store and validated operation mode.
    /// </summary>
    internal DeckBatchWriteTools(SqliteDeckStore store, OperationMode mode)
    {
        this.store = store;
        this.mode = mode;
    }

    /// <summary>
    /// Creates the explicitly registered tool with a description on its synthetic discriminator field.
    /// </summary>
    internal static RegisteredTool Create(SqliteDeckStore store, OperationMode mode)
    {
        DeckBatchWriteTools target = new(store, mode);
        MethodInfo method = typeof(DeckBatchWriteTools).GetMethod(
            nameof(ApplyChangesAsync),
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ToolRegistration.Create(method, target, describeNode: DescribeDiscriminator);
    }

    /// <summary>
    /// Applies an explicit ordered batch atomically through the shared transaction path.
    /// </summary>
    [McpServerTool(
        Name = "local_deck_apply_changes",
        Title = "Apply Local Deck Changes",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Local deck store: applies an ordered batch of changes to one local deck atomically; any invalid " +
        "change rolls back the whole batch. This is the only tool that edits an existing deck, and a batch of " +
        "one change is a normal use. Each change has a kind: update-metadata (name, description, format), " +
        "add-entry, update-entry (the complete replacement entry with its entryId), remove-entry, " +
        "add-category, update-category, remove-category, assign-category, unassign-category, " +
        "upsert-provider-binding, or remove-provider-binding. A new entry or category may carry its own ID, " +
        "so later changes in the same batch can refer to it." +
        DeckToolExamples.ExamplePrefix + "minimal): " + DeckToolExamples.ApplyChangesMinimal +
        DeckToolExamples.ExamplePrefix + "categorize): " + DeckToolExamples.ApplyChangesCategorize +
        DeckToolExamples.ExamplePrefix + "full): " + DeckToolExamples.ApplyChangesFull)]
    internal Task<OperationResult<DeckDocument>> ApplyChangesAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Ordered closed-union mutations applied in one transaction.")]
        IReadOnlyList<DeckChangeInput>? changes,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return Task.FromResult<OperationResult<DeckDocument>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit local writes."));
        }

        if (!DeckChangeInputMapper.TryMap(changes, out IReadOnlyList<DeckChange> mapped, out string failure))
        {
            return Task.FromResult<OperationResult<DeckDocument>>(
                new OperationInvalidInput("invalid-deck-change", failure));
        }

        return store.ApplyChangesAsync(deckId, expectedRevision, mapped, cancellationToken);
    }

    /// <summary>
    /// Adds a useful description to generated polymorphic discriminator schemas.
    /// </summary>
    private static JsonNode DescribeDiscriminator(JsonNode schema)
    {
        if (schema is JsonObject schemaObject &&
            schemaObject["const"] is JsonValue constant &&
            constant.TryGetValue(out string? value) &&
            value is not null &&
            ChangeKinds.Contains(value))
        {
            schemaObject["description"] = "Selects the exact closed deck-change variant.";
        }

        return schema;
    }
}
