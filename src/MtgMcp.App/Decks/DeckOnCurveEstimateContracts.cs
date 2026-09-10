using System.ComponentModel;
using System.Text.Json.Serialization;
using MtgMcp.Core.Evidence;

namespace MtgMcp.App.Decks;

/// <summary>
/// Requests one repeatable sampled estimate for a target card in a saved deck.
/// </summary>
internal sealed record DeckOnCurveEstimateToolRequest(
    [property: Description("Stable local deck UUID returned by deck_get.")] Guid DeckId,
    [property: Description("The exact deck revision returned by deck_get; a changed deck returns a conflict.")]
    long ExpectedRevision,
    [property: Description("Stable ID of one target entry in the deck mainboard.")] Guid TargetEntryId,
    [property: Description("Last turn to model, from 1 through 12.")] int TurnLimit,
    [property: Description("One stated rule for every eligible single-faced land in the mainboard.")]
    IReadOnlyList<DeckOnCurveLandRuleToolInput>? LandRules,
    [property: Description("Number of shuffled hands to sample, from 100 through 100000. Default: 10000.")]
    int SampleCount = 10_000,
    [property: Description("True skips the normal draw on turn one. Default: true.")] bool OnThePlay = true,
    [property: Description("Optional 16-character hexadecimal replay seed. A missing seed is generated and returned.")]
    string? Seed = null);

/// <summary>
/// States how the estimate may use one caller-identified eligible land.
/// </summary>
internal sealed record DeckOnCurveLandRuleToolInput(
    [property: Description("Stable mainboard entry UUID for one eligible land.")] Guid EntryId,
    [property: Description("Exactly one of one-mana-same-turn, one-mana-next-turn, or not-modeled.")]
    string Rule);

/// <summary>
/// Returns one fact-backed sampled estimate without a deckbuilding recommendation.
/// </summary>
internal sealed record DeckOnCurveEstimateResult(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("modelVersion")] string ModelVersion,
    [property: JsonPropertyName("policyId")] string PolicyId,
    [property: JsonPropertyName("randomVersion")] string RandomVersion,
    [property: JsonPropertyName("deckId")] Guid DeckId,
    [property: JsonPropertyName("deckRevision")] long DeckRevision,
    [property: JsonPropertyName("target")] DeckOnCurveTargetResult Target,
    [property: JsonPropertyName("sourceFacts")] IReadOnlyList<DeckOnCurveSourceFact> SourceFacts,
    [property: JsonPropertyName("sourceEvidence")] DeckOnCurveSourceEvidence SourceEvidence,
    [property: JsonPropertyName("landRuleCoverage")] DeckOnCurveLandRuleCoverage LandRuleCoverage,
    [property: JsonPropertyName("inputFingerprint")] string InputFingerprint,
    [property: JsonPropertyName("seed")] string Seed,
    [property: JsonPropertyName("sampleCount")] int SampleCount,
    [property: JsonPropertyName("completedTrialCount")] int CompletedTrialCount,
    [property: JsonPropertyName("turnLimit")] int TurnLimit,
    [property: JsonPropertyName("onThePlay")] bool OnThePlay,
    [property: JsonPropertyName("successCount")] int SuccessCount,
    [property: JsonPropertyName("successRate")] double SuccessRate,
    [property: JsonPropertyName("interval")] DeckOnCurveInterval Interval,
    [property: JsonPropertyName("failureCounts")] IReadOnlyList<DeckOnCurveFailureCount> FailureCounts,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("traces")] IReadOnlyList<DeckOnCurveTrace> Traces);

/// <summary>
/// Identifies the exact target card fact used by one estimate.
/// </summary>
internal sealed record DeckOnCurveTargetResult(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("printingId")] Guid PrintingId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("manaCost")] string? ManaCost);

/// <summary>
/// Preserves one direct Scryfall fact used to build the modeled deck.
/// </summary>
internal sealed record DeckOnCurveSourceFact(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("printingId")] Guid PrintingId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("manaCost")] string? ManaCost,
    [property: JsonPropertyName("typeLine")] string? TypeLine,
    [property: JsonPropertyName("isSingleFaced")] bool IsSingleFaced,
    [property: JsonPropertyName("producedMana")] DeckOnCurveProducedManaFact ProducedMana,
    [property: JsonPropertyName("evidence")] EvidenceDescriptor Evidence);

/// <summary>
/// Preserves whether Scryfall omitted, nullified, or listed produced-mana values.
/// </summary>
internal sealed record DeckOnCurveProducedManaFact(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("colors")] IReadOnlyList<string>? Colors);

/// <summary>
/// Identifies the installed Scryfall card-data generation used by the estimate.
/// </summary>
internal sealed record DeckOnCurveSourceEvidence(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("installedCardDataGenerationId")] Guid InstalledCardDataGenerationId,
    [property: JsonPropertyName("evidenceChecksum")] string EvidenceChecksum);

/// <summary>
/// Shows every land candidate and every card left outside the version-one land model.
/// </summary>
internal sealed record DeckOnCurveLandRuleCoverage(
    [property: JsonPropertyName("candidateLands")] IReadOnlyList<DeckOnCurveCandidateLand> CandidateLands,
    [property: JsonPropertyName("knownEmptyLands")] IReadOnlyList<DeckOnCurveKnownEmptyLand> KnownEmptyLands,
    [property: JsonPropertyName("multiFaceCards")] IReadOnlyList<DeckOnCurveMultiFaceCard> MultiFaceCards);

/// <summary>
/// Shows one eligible land's source fact and the caller rule used in the estimate.
/// </summary>
internal sealed record DeckOnCurveCandidateLand(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("printingId")] Guid PrintingId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("typeLine")] string? TypeLine,
    [property: JsonPropertyName("producedMana")] DeckOnCurveProducedManaFact ProducedMana,
    [property: JsonPropertyName("rule")] string Rule);

/// <summary>
/// Shows a single-faced land whose direct source fact listed no mana values.
/// </summary>
internal sealed record DeckOnCurveKnownEmptyLand(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("printingId")] Guid PrintingId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("typeLine")] string? TypeLine,
    [property: JsonPropertyName("producedMana")] DeckOnCurveProducedManaFact ProducedMana);

/// <summary>
/// Shows one multi-face mainboard card left outside the version-one model.
/// </summary>
internal sealed record DeckOnCurveMultiFaceCard(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("printingId")] Guid PrintingId,
    [property: JsonPropertyName("name")] string Name);

/// <summary>
/// Names the interval method and its unrounded lower and upper bounds.
/// </summary>
internal sealed record DeckOnCurveInterval(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("lowerBound")] double LowerBound,
    [property: JsonPropertyName("upperBound")] double UpperBound);

/// <summary>
/// Counts one mutually exclusive final reason a sampled hand missed the target.
/// </summary>
internal sealed record DeckOnCurveFailureCount(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("count")] int Count);

/// <summary>
/// Preserves one bounded successful or unsuccessful sampled hand trace.
/// </summary>
internal sealed record DeckOnCurveTrace(
    [property: JsonPropertyName("trialNumber")] int TrialNumber,
    [property: JsonPropertyName("succeeded")] bool Succeeded,
    [property: JsonPropertyName("events")] IReadOnlyList<DeckOnCurveTraceEvent> Events,
    [property: JsonPropertyName("omittedEventCount")] int OmittedEventCount);

/// <summary>
/// Preserves one visible event from a sampled hand without interpreting card text.
/// </summary>
internal sealed record DeckOnCurveTraceEvent(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("entryId")] Guid? EntryId,
    [property: JsonPropertyName("landRule")] string? LandRule,
    [property: JsonPropertyName("missReason")] string? MissReason,
    [property: JsonPropertyName("paymentSources")] IReadOnlyList<DeckOnCurvePaymentSource> PaymentSources);

/// <summary>
/// Shows one modeled land that paid one printed target-cost symbol in a trace.
/// </summary>
internal sealed record DeckOnCurvePaymentSource(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("paidFor")] string PaidFor);
