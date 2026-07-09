using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
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
    internal static McpServerTool Create(SqliteDeckStore store, OperationMode mode)
    {
        DeckBatchWriteTools target = new(store, mode);
        MethodInfo method = typeof(DeckBatchWriteTools).GetMethod(
            nameof(ApplyChangesAsync),
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return McpServerTool.Create(method, target, new McpServerToolCreateOptions
        {
            SchemaCreateOptions = new AIJsonSchemaCreateOptions
            {
                TransformSchemaNode = static (_, schema) => DescribeDiscriminator(schema),
                TransformOptions = new AIJsonSchemaTransformOptions
                {
                    TransformSchemaNode = static (_, schema) => DescribeDiscriminator(schema),
                },
            },
        });
    }

    /// <summary>
    /// Applies an explicit ordered batch atomically through the shared transaction path.
    /// </summary>
    [McpServerTool(
        Name = "deck_apply_changes",
        Title = "Apply Local Deck Changes",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Applies an ordered batch atomically; any invalid change rolls back the full batch.")]
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
