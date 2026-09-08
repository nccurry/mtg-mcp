using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>Returns a deterministic category proposal and evidence.</summary>
/// <param name="DeckId">The local deck identifier.</param>
/// <param name="DeckRevision">The revision evaluated.</param>
/// <param name="Source">The explicit caller rule source.</param>
/// <param name="ExpandedRules">The canonical expanded rules.</param>
/// <param name="Decisions">The ordered per-entry decisions.</param>
/// <param name="IsComplete">Whether all evidence was complete.</param>
/// <param name="PreviewFingerprint">The evidence-bound preview fingerprint.</param>
/// <param name="ApplyToken">The opaque token required for apply.</param>
/// <param name="CorpusGenerationId">The Scryfall generation used.</param>
/// <param name="PresetSchemaVersion">The preset schema version when applicable.</param>
/// <param name="PresetChecksum">The immutable preset checksum when applicable.</param>
internal sealed record DeckCategoryRulesPreview(
    [property: JsonPropertyName("deckId")] Guid DeckId,
    [property: JsonPropertyName("deckRevision")] long DeckRevision,
    [property: JsonPropertyName("source")] CategoryRuleSource Source,
    [property: JsonPropertyName("expandedRules")] CategoryRuleSet ExpandedRules,
    [property: JsonPropertyName("decisions")] IReadOnlyList<CategoryDecision> Decisions,
    [property: JsonPropertyName("isComplete")] bool IsComplete,
    [property: JsonPropertyName("previewFingerprint")] string PreviewFingerprint,
    [property: JsonPropertyName("applyToken")] string ApplyToken,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId,
    [property: JsonPropertyName("presetSchemaVersion")] int? PresetSchemaVersion,
    [property: JsonPropertyName("presetChecksum")] string? PresetChecksum);

/// <summary>Coordinates category rules, local decks, and shared Scryfall evidence.</summary>
[ExcludeFromCodeCoverage(Justification = "Provider composition is verified through App and official-client integration tests; deterministic evaluation is covered in Core.")]
internal sealed class DeckCategorizationCoordinator
{
    /// <summary>Uses deterministic web JSON for fingerprints.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    /// <summary>Owns revisioned local deck mutations.</summary>
    private readonly SqliteDeckStore deckStore;
    /// <summary>Reads generation-bound Scryfall tag evidence for deck entries.</summary>
    private readonly ScryfallService scryfall;
    /// <summary>Expands and resolves category rules against installed Scryfall tag data.</summary>
    private readonly DeckCategoryRuleResolver ruleResolver;

    /// <summary>Creates the categorization coordinator.</summary>
    internal DeckCategorizationCoordinator(SqliteDeckStore deckStore, ScryfallService scryfall)
    {
        this.deckStore = deckStore;
        this.scryfall = scryfall;
        ruleResolver = new DeckCategoryRuleResolver(scryfall);
    }

    /// <summary>Validates and expands a rule source without mutating the deck.</summary>
    internal async Task<OperationResult<DeckCategoryRulesPreview>> ValidateAsync(
        Guid deckId,
        CategoryRuleSource source,
        string freshnessPolicy,
        CancellationToken cancellationToken)
    {
        OperationResult<DeckCategoryRulesPreview> result = await BuildPreviewAsync(
            deckId, null, source, freshnessPolicy, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Builds a fingerprinted category proposal without mutation.</summary>
    internal Task<OperationResult<DeckCategoryRulesPreview>> PreviewAsync(
        Guid deckId,
        long expectedRevision,
        CategoryRuleSource source,
        string freshnessPolicy,
        CancellationToken cancellationToken)
    {
        return BuildPreviewAsync(deckId, expectedRevision, source, freshnessPolicy, cancellationToken);
    }

    /// <summary>Recomputes and applies an unchanged proposal in one deck revision.</summary>
    internal async Task<OperationResult<DeckDocument>> ApplyAsync(
        Guid deckId,
        long expectedRevision,
        Guid? expectedCorpusGeneration,
        CategoryRuleSource source,
        string freshnessPolicy,
        string previewFingerprint,
        string applyToken,
        CancellationToken cancellationToken)
    {
        string expectedToken = Hash(JsonSerializer.Serialize(new { deckId, expectedRevision, previewFingerprint }, SerializerOptions));
        if (!string.Equals(expectedToken, applyToken, StringComparison.Ordinal))
        {
            return new OperationInvalidInput("invalid-category-apply-token", "The category apply token is invalid.");
        }

        OperationResult<DeckCategoryRulesPreview> previewResult = await BuildPreviewAsync(
            deckId, expectedRevision, source, freshnessPolicy, cancellationToken).ConfigureAwait(false);
        if (previewResult is not OperationSuccess<DeckCategoryRulesPreview> preview)
        {
            return ForwardFailure<DeckCategoryRulesPreview, DeckDocument>(previewResult);
        }

        if (!string.Equals(preview.Data.PreviewFingerprint, previewFingerprint, StringComparison.Ordinal))
        {
            return new OperationConflict("category-preview-mismatch", "The category evidence no longer reproduces the preview.");
        }

        if (preview.Data.CorpusGenerationId != expectedCorpusGeneration)
        {
            return new OperationConflict("category-corpus-conflict", "The Scryfall card-data generation changed after preview.");
        }

        if (!preview.Data.IsComplete)
        {
            return new OperationInvalidInput("category-preview-incomplete", "Unknown evidence must be resolved before apply.");
        }

        OperationResult<DeckDocument> deckResult = await deckStore.GetAsync(deckId, cancellationToken).ConfigureAwait(false);
        if (deckResult is not OperationSuccess<DeckDocument> deck)
        {
            return deckResult;
        }

        List<DeckChange> changes = BuildChanges(deck.Data, preview.Data);
        return changes.Count == 0
            ? deckResult
            : await deckStore.ApplyChangesAsync(deckId, expectedRevision, changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds one preview after validating the deck and source.</summary>
    private async Task<OperationResult<DeckCategoryRulesPreview>> BuildPreviewAsync(
        Guid deckId,
        long? expectedRevision,
        CategoryRuleSource source,
        string freshnessPolicy,
        CancellationToken cancellationToken)
    {
        OperationResult<DeckDocument> deckResult = await deckStore.GetAsync(deckId, cancellationToken).ConfigureAwait(false);
        if (deckResult is not OperationSuccess<DeckDocument> deck)
        {
            return ForwardFailure<DeckDocument, DeckCategoryRulesPreview>(deckResult);
        }

        if (expectedRevision is long revision && (revision <= 0 || revision != deck.Data.Revision))
        {
            return new OperationConflict("deck-revision-conflict", "The local deck revision changed before categorization.");
        }

        OperationResult<ResolvedDeckCategoryRules> resolution = await ruleResolver.ResolveAsync(
            source,
            deck.Data,
            freshnessPolicy,
            cancellationToken).ConfigureAwait(false);
        if (resolution is not OperationSuccess<ResolvedDeckCategoryRules> resolvedRules)
        {
            return ForwardFailure<ResolvedDeckCategoryRules, DeckCategoryRulesPreview>(resolution);
        }

        List<CategoryEntryEvidence> evidence = [];
        IReadOnlyList<DeckEntry> entries = deck.Data.Entries.OrderBy(value => value.SortOrder).ThenBy(value => value.EntryId).ToArray();
        if (resolvedRules.Data.ScryfallGenerationId is not Guid generation)
        {
            foreach (DeckEntry entry in entries)
            {
                evidence.Add(new CategoryEntryEvidence(entry.EntryId, []));
            }
        }
        else
        {
            OperationResult<ScryfallDeckTagEvidence> sourceEvidence = await scryfall.ReadDeckTagEvidenceAsync(
                generation,
                entries.Select(CreateLookup).ToArray(),
                cancellationToken).ConfigureAwait(false);
            if (sourceEvidence is not OperationSuccess<ScryfallDeckTagEvidence> tags)
            {
                return ForwardFailure<ScryfallDeckTagEvidence, DeckCategoryRulesPreview>(sourceEvidence);
            }

            if (tags.Data.Entries.Count != entries.Count || tags.Data.GenerationId != generation)
            {
                return new OperationUnavailable(
                    "scryfall-tag-evidence-mismatch",
                    "Scryfall returned incomplete category-rule tag evidence.");
            }

            for (int index = 0; index < entries.Count; index++)
            {
                ScryfallDeckTagEntryEvidence entryEvidence = tags.Data.Entries[index];
                evidence.Add(new CategoryEntryEvidence(
                    entries[index].EntryId,
                    entryEvidence.Tags.Select(tag => new CategoryTagEvidence(
                        tag.TagId,
                        tag.TagType,
                        tag.Slug,
                        tag.Weight,
                        tag.AncestorTagIds)).ToArray(),
                    entryEvidence.IsComplete));
            }
        }

        CategoryEvaluation evaluation = DeckCategorizationEvaluator.Evaluate(
            resolvedRules.Data.Rules,
            evidence,
            deck.Data.CategoryAssignments);
        bool complete = evaluation.Decisions.All(value => value.Status != "unknown");
        string fingerprint = Hash(JsonSerializer.Serialize(new
        {
            deckId,
            revision = deck.Data.Revision,
            source,
            rules = resolvedRules.Data.Rules,
            decisions = evaluation.Decisions,
            generation = resolvedRules.Data.ScryfallGenerationId,
        }, SerializerOptions));
        string token = Hash(JsonSerializer.Serialize(new { deckId, expectedRevision = deck.Data.Revision, previewFingerprint = fingerprint }, SerializerOptions));
        return new OperationSuccess<DeckCategoryRulesPreview>(new DeckCategoryRulesPreview(
            deckId, deck.Data.Revision, source, resolvedRules.Data.Rules, evaluation.Decisions,
            complete, fingerprint, token, resolvedRules.Data.ScryfallGenerationId,
            resolvedRules.Data.PresetSchemaVersion, resolvedRules.Data.PresetChecksum));
    }

    /// <summary>Creates the strongest stable local lookup for one deck entry.</summary>
    private static ScryfallCardLookup CreateLookup(DeckEntry entry)
    {
        return entry.PrintingId is Guid printing
            ? new ScryfallCardLookup("scryfall-id", printing.ToString("D"))
            : entry.OracleId is Guid oracle
                ? new ScryfallCardLookup("oracle-id", oracle.ToString("D"))
                : new ScryfallCardLookup("exact-name", entry.CardName);
    }

    /// <summary>Builds the minimal ordered assignment mutation set.</summary>
    private static List<DeckChange> BuildChanges(DeckDocument deck, DeckCategoryRulesPreview preview)
    {
        List<DeckChange> changes = [];
        HashSet<(Guid EntryId, Guid CategoryId)> target = preview.Decisions
            .Where(value => value.Status == "matched")
            .Select(value => (value.EntryId, value.CategoryId))
            .ToHashSet();
        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            if (preview.ExpandedRules.AssignmentMode == "synchronize-listed-categories" &&
                preview.ExpandedRules.Rules.Any(value => value.CategoryId == assignment.CategoryId) &&
                !target.Contains((assignment.EntryId, assignment.CategoryId)))
            {
                changes.Add(new UnassignDeckCategoryChange(assignment.EntryId, assignment.CategoryId));
            }
        }

        foreach ((Guid entryId, Guid categoryId) in target)
        {
            if (!deck.CategoryAssignments.Any(value => value.EntryId == entryId && value.CategoryId == categoryId))
            {
                changes.Add(new AssignDeckCategoryChange(entryId, categoryId, false));
            }
        }

        return changes;
    }

    /// <summary>Hashes canonical evidence and preview state.</summary>
    private static string Hash(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>Maps a non-success result to the categorization result type.</summary>
    private static OperationResult<T> ForwardFailure<TSource, T>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            OperationSuccess<TSource> => new OperationUnavailable("unexpected-result", "The operation returned an unexpected result."),
            _ => new OperationUnavailable("unexpected-result", "The operation returned an unexpected result."),
        };
    }
}

/// <summary>Registers deterministic category validation and preview tools.</summary>
[ExcludeFromCodeCoverage(Justification = "MCP attribute wrappers are exercised by official-client E2E surface tests.")]
internal sealed class DeckCategorizationReadTools
{
    /// <summary>Coordinates deck and Scryfall evidence operations.</summary>
    private readonly DeckCategorizationCoordinator coordinator;

    /// <summary>Creates read tools around the category coordinator.</summary>
    internal DeckCategorizationReadTools(DeckCategorizationCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>Validates an inline rule set or explicit common-v1 preset.</summary>
    [McpServerTool(Name = "deck_category_rules_validate", Title = "Validate Deck Category Rules", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Validates explicit inline rules or the common-v1 preset without changing the deck.")]
    internal Task<OperationResult<DeckCategoryRulesPreview>> ValidateAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Explicit inline or preset category rule source.")] CategoryRuleSource source,
        [Description("default, cache-only, or refresh Scryfall evidence policy.")] string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        return coordinator.ValidateAsync(deckId, source, freshnessPolicy, cancellationToken);
    }

    /// <summary>Previews exact category changes without mutation.</summary>
    [McpServerTool(Name = "deck_category_rules_preview", Title = "Preview Deck Category Rules", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Evaluates explicit category rules and returns evidence, unknowns, and an apply fingerprint.")]
    internal Task<OperationResult<DeckCategoryRulesPreview>> PreviewAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
         [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
         [Description("Explicit inline or preset category rule source.")] CategoryRuleSource source,
        [Description("default, cache-only, or refresh Scryfall evidence policy.")] string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        return coordinator.PreviewAsync(deckId, expectedRevision, source, freshnessPolicy, cancellationToken);
    }
}

/// <summary>Registers guarded category application.</summary>
[ExcludeFromCodeCoverage(Justification = "MCP attribute wrapper is exercised by official-client E2E surface tests.")]
internal sealed class DeckCategorizationWriteTools
{
    /// <summary>Coordinates deck and Scryfall evidence operations.</summary>
    private readonly DeckCategorizationCoordinator coordinator;
    /// <summary>Provides the effective operation authority.</summary>
    private readonly OperationMode mode;

    /// <summary>Creates write tools around the category coordinator.</summary>
    internal DeckCategorizationWriteTools(DeckCategorizationCoordinator coordinator, OperationMode mode)
    {
        this.coordinator = coordinator;
        this.mode = mode;
    }

    /// <summary>Applies an unchanged complete category preview.</summary>
    [McpServerTool(Name = "deck_category_rules_apply", Title = "Apply Deck Category Rules", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Applies only an unchanged, complete category preview in one local deck revision.")]
    internal Task<OperationResult<DeckDocument>> ApplyAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Scryfall card-data generation returned by the preview, or null when no card evidence was needed.")] Guid? expectedCorpusGeneration,
        [Description("Explicit inline or preset category rule source.")] CategoryRuleSource source,
        [Description("default, cache-only, or refresh Scryfall evidence policy.")] string freshnessPolicy,
        [Description("Fingerprint returned by deck_category_rules_preview.")] string previewFingerprint,
        [Description("Opaque token returned by deck_category_rules_preview.")] string applyToken,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return Task.FromResult<OperationResult<DeckDocument>>(
                new OperationUnsupported("operation-mode-denied", "The effective operation mode does not permit local writes."));
        }

        return coordinator.ApplyAsync(deckId, expectedRevision, expectedCorpusGeneration, source, freshnessPolicy, previewFingerprint, applyToken, cancellationToken);
    }
}
