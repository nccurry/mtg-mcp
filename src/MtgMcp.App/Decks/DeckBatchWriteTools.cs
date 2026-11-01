using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
    /// Stores the lookup that fills in the card identity of added entries.
    /// </summary>
    private readonly DeckEntryResolver resolver;

    /// <summary>
    /// Creates the batch tool around one store, validated operation mode, and the entry resolver.
    /// </summary>
    internal DeckBatchWriteTools(SqliteDeckStore store, OperationMode mode, DeckEntryResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.store = store;
        this.mode = mode;
        this.resolver = resolver;
    }

    /// <summary>
    /// Creates the explicitly registered tool with a description on its synthetic discriminator field.
    /// </summary>
    internal static RegisteredTool Create(SqliteDeckStore store, OperationMode mode, DeckEntryResolver resolver)
    {
        DeckBatchWriteTools target = new(store, mode, resolver);
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
        "so later changes in the same batch can refer to it. An added entry that has only a card name (or a set code " +
        "and collector number) gets its card identity from installed card data; a name that is not found is " +
        "still added and listed in issues. An add of a card already in the deck still makes another entry and is " +
        "listed as already-in-deck; a retried add should pass an entryId or set whenCardPresent to skip, which " +
        "leaves out an add whose card is already in the same zone. The result is a summary (new revision, counts, " +
        "IDs of changed entries and categories, issues); pass returnDeck true to also get the whole deck." +
        UntrustedTextNotes.CardListEntries +
        DeckToolExamples.ExamplePrefix + "minimal): " + DeckToolExamples.ApplyChangesMinimal +
        DeckToolExamples.ExamplePrefix + "categorize): " + DeckToolExamples.ApplyChangesCategorize +
        DeckToolExamples.ExamplePrefix + "full): " + DeckToolExamples.ApplyChangesFull)]
    internal async Task<OperationResult<DeckWriteResult>> ApplyChangesAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Ordered closed-union mutations applied in one transaction.")]
        IReadOnlyList<DeckChangeInput>? changes,
        [Description("Set true to also return the whole deck after the write. The default summary is much smaller.")]
        bool returnDeck = false,
        [Description(
            "What to do with an add-entry whose card is already in the same zone: add (default) makes another " +
            "entry and reports already-in-deck; skip leaves the add out and reports skipped."),
         AllowedValues(DeckAddPresence.Add, DeckAddPresence.Skip)]
        string whenCardPresent = DeckAddPresence.Add,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return new OperationUnsupported(
                "operation-mode-denied",
                "The effective operation mode does not permit local writes.");
        }

        if (whenCardPresent is not (DeckAddPresence.Add or DeckAddPresence.Skip))
        {
            return new OperationInvalidInput(
                "invalid-when-card-present",
                "whenCardPresent must be \"add\" or \"skip\".");
        }

        if (!DeckChangeInputMapper.TryMap(changes, out IReadOnlyList<DeckChange> mapped, out string failure))
        {
            return new OperationInvalidInput("invalid-deck-change", failure);
        }

        OperationResult<DeckDocument> read = await store.GetAsync(deckId, cancellationToken).ConfigureAwait(false);
        if (read is not OperationSuccess<DeckDocument> before)
        {
            return OperationFailure.Forward<DeckDocument, DeckWriteResult>(read);
        }

        // The apply below succeeds only when the deck is still at expectedRevision, and every write raises the
        // revision. So when the revision read here matches, the summary compares the state the apply starts from;
        // when it differs the apply is refused and no summary is built, so the read needs no transaction of its own.
        IReadOnlyList<DeckWriteIssue> issues = [];
        if (before.Data.Revision == expectedRevision)
        {
            DeckChangeResolution resolution = await resolver.ResolveChangesAsync(mapped, cancellationToken)
                .ConfigureAwait(false);
            DeckAddPresenceResult presence = DeckAddPresence.Check(
                before.Data,
                resolution.Changes,
                whenCardPresent == DeckAddPresence.Skip);
            if (presence.Refusal is not null)
            {
                return new OperationInvalidInput("invalid-deck-change", presence.Refusal);
            }

            issues = [.. resolution.Issues, .. presence.Issues];
            if (presence.Changes.Count == 0)
            {
                // Every change was a skipped add, so nothing is written and the revision stays as it is.
                return new OperationSuccess<DeckWriteResult>(
                    DeckWriteResults.After(before.Data, before.Data, issues, returnDeck));
            }

            mapped = presence.Changes;
        }

        OperationResult<DeckDocument> applied = await store
            .ApplyChangesAsync(deckId, expectedRevision, mapped, cancellationToken).ConfigureAwait(false);
        return applied is OperationSuccess<DeckDocument> after
            ? new OperationSuccess<DeckWriteResult>(
                DeckWriteResults.After(before.Data, after.Data, issues, returnDeck))
            : OperationFailure.Forward<DeckDocument, DeckWriteResult>(applied);
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
