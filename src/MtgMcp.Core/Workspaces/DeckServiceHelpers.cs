using System.Globalization;

namespace MtgMcp.Core;

/// <summary>
/// Provides shared card snapshot, role-count, and plan helpers without putting them on the service base.
/// </summary>
internal static class DeckServiceHelpers
{
    /// <summary>
    /// Refreshes Scryfall-backed snapshots after this age even when required fields are present.
    /// </summary>
    private static readonly TimeSpan SnapshotStaleAfter = TimeSpan.FromDays(30);

    /// <summary>
    /// Refreshes cached card snapshots for cards matching a normalized scope.
    /// </summary>
    public static async Task<DeckNormalizationResult> NormalizeWorkspaceCardsAsync(
        ICardCatalog cardCatalog,
        DeckWorkspace workspace,
        string normalizedScope,
        CancellationToken cancellationToken)
    {
        List<DeckCard> targetCards = [];
        foreach (DeckCard card in workspace.Cards)
        {
            if (ShouldNormalize(card, workspace, normalizedScope))
            {
                targetCards.Add(card);
            }
        }

        List<string> targetNames = [];
        foreach (DeckCard card in targetCards)
        {
            targetNames.Add(card.Name);
        }

        IReadOnlyDictionary<string, CardInfo> cardsByName = await cardCatalog
            .GetCardsByNamesAsync(targetNames, cancellationToken)
            .ConfigureAwait(false);

        List<string> missingCards = [];
        CardSnapshotQualitySummary qualityBefore = BuildSnapshotQualitySummary(targetCards);
        int updatedCards = 0;
        int unchangedCards = 0;
        foreach (DeckCard card in targetCards)
        {
            if (!cardsByName.TryGetValue(card.Name, out CardInfo? cardInfo))
            {
                missingCards.Add(card.Name);
                continue;
            }

            CardSnapshotFingerprint before = CardSnapshotFingerprint.From(card);
            card.ScryfallId = cardInfo.Id;
            card.ScryfallOracleId = cardInfo.OracleId;
            ApplyCardSnapshot(card, cardInfo);
            CardSnapshotFingerprint after = CardSnapshotFingerprint.From(card);
            if (before.Equals(after))
            {
                unchangedCards++;
            }
            else
            {
                updatedCards++;
            }
        }

        return new DeckNormalizationResult
        {
            WorkspaceId = workspace.Id,
            Scope = normalizedScope,
            RequestedCards = targetCards.Count,
            UpdatedCards = updatedCards,
            UnchangedCards = unchangedCards,
            MissingCards = missingCards,
            FailedCards = [],
            SnapshotQualityBefore = qualityBefore,
            SnapshotQualityAfter = BuildSnapshotQualitySummary(targetCards),
            Workspace = workspace
        };
    }

    /// <summary>
    /// Determines whether a card should be normalized.
    /// </summary>
    public static bool ShouldNormalize(DeckCard card, DeckWorkspace workspace, string scope)
    {
        return scope switch
        {
            "all" => true,
            "included" => IsIncluded(workspace, card),
            "maybeboard" => string.Equals(DeckCategoryOrdering.PrimaryCategory(card), DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase),
            "missing" => IsMissingScryfallSnapshot(card),
            "stale" => !IsMissingScryfallSnapshot(card) && IsStaleSnapshot(card, workspace),
            "needed" => IsMissingScryfallSnapshot(card) || IsStaleSnapshot(card, workspace),
            _ => true
        };
    }

    /// <summary>
    /// Checks whether a card lacks a resolvable Scryfall-backed snapshot identity.
    /// </summary>
    public static bool IsMissingScryfallSnapshot(DeckCard card)
    {
        return card.Snapshot is null || string.IsNullOrWhiteSpace(card.ScryfallId);
    }

    /// <summary>
    /// Checks whether a snapshot exists but is incomplete or past its freshness window.
    /// </summary>
    public static bool IsStaleSnapshot(DeckCard card, DeckWorkspace workspace)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        if (!IsScryfallSnapshot(snapshot))
        {
            return true;
        }

        if (!snapshot.Provenance.RefreshedAtUtc.HasValue
            || DateTimeOffset.UtcNow - snapshot.Provenance.RefreshedAtUtc.Value > SnapshotStaleAfter)
        {
            return true;
        }

        return !HasRequiredSnapshotFields(card, workspace, snapshot);
    }

    /// <summary>
    /// Checks whether a snapshot was sourced from Scryfall metadata.
    /// </summary>
    private static bool IsScryfallSnapshot(CardSnapshot snapshot)
    {
        return snapshot.Provenance.Provider?.Equals("scryfall", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Checks whether the snapshot has fields needed by analysis, pricing, legality, and display.
    /// </summary>
    private static bool HasRequiredSnapshotFields(DeckCard card, DeckWorkspace workspace, CardSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.TypeLine))
        {
            return false;
        }

        if (NeedsManaMetadata(card, workspace, snapshot)
            && string.IsNullOrWhiteSpace(snapshot.OracleText))
        {
            return false;
        }

        if (NeedsProducedManaMetadata(card, snapshot) && snapshot.ProducedMana.Count == 0)
        {
            return false;
        }

        return snapshot.Prices.Count > 0
            && !string.IsNullOrWhiteSpace(snapshot.SelectedPrintingReason)
            && !string.IsNullOrWhiteSpace(snapshot.ScryfallUri)
            && snapshot.Legalities.Count > 0
            && snapshot.ReleasedAt.HasValue
            && !string.IsNullOrWhiteSpace(snapshot.Language);
    }

    /// <summary>
    /// Checks whether analyses need produced-mana facts for this card.
    /// </summary>
    private static bool NeedsManaMetadata(DeckCard card, DeckWorkspace workspace, CardSnapshot snapshot)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        return primaryCategory.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            || primaryCategory.Equals("Land", StringComparison.OrdinalIgnoreCase)
            || primaryCategory.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
            || (snapshot.TypeLine?.Contains("Land", StringComparison.OrdinalIgnoreCase) == true)
            || (snapshot.OracleText?.Contains("add ", StringComparison.OrdinalIgnoreCase) == true)
            || IsIncluded(workspace, card)
                && (snapshot.OracleText?.Contains("add one mana", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Checks whether Scryfall should have supplied concrete produced-mana symbols.
    /// </summary>
    private static bool NeedsProducedManaMetadata(DeckCard card, CardSnapshot snapshot)
    {
        return IsBasicLandName(card.Name)
            || ContainsManaProductionText(snapshot.OracleText);
    }

    /// <summary>
    /// Checks for oracle phrases that describe adding mana.
    /// </summary>
    private static bool ContainsManaProductionText(string? oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText))
        {
            return false;
        }

        return oracleText.Contains("add {", StringComparison.OrdinalIgnoreCase)
            || oracleText.Contains("add one mana", StringComparison.OrdinalIgnoreCase)
            || oracleText.Contains("add two mana", StringComparison.OrdinalIgnoreCase)
            || oracleText.Contains("add three mana", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the card name is one of the six basic land names.
    /// </summary>
    private static bool IsBasicLandName(string name)
    {
        return name.Equals("Plains", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Island", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Swamp", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Mountain", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Forest", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Wastes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enumerates included workspace cards.
    /// </summary>
    public static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        return DeckCategoryInclusion.IncludedCards(workspace);
    }

    /// <summary>
    /// Determines whether a card is included in the deck.
    /// </summary>
    public static bool IsIncluded(DeckWorkspace workspace, DeckCard card)
    {
        return DeckCategoryInclusion.IsIncludedInDeck(workspace, card);
    }

    /// <summary>
    /// Parses draw odds targets.
    /// </summary>
    public static List<string> ParseTargets(string? targets, DeckIntent? intent)
    {
        if (string.IsNullOrWhiteSpace(targets))
        {
            if (intent?.Targets.Count > 0)
            {
                List<string> parsedTargets = [];
                foreach (string target in intent.Targets.Keys)
                {
                    bool knownTarget = DeckRoles.Primary.Contains(target, StringComparer.OrdinalIgnoreCase)
                        || DeckTags.Secondary.Contains(target, StringComparer.OrdinalIgnoreCase);
                    if (knownTarget && !parsedTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        parsedTargets.Add(target);
                    }
                }

                return parsedTargets;
            }

            return
            [
                DeckRoles.Lands,
                DeckRoles.Ramp,
                DeckRoles.Draw,
                DeckRoles.Interaction,
                DeckRoles.BoardWipes,
                DeckTags.Discard
            ];
        }

        List<string> requestedTargets = [];
        foreach (string target in targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(target)
                && !requestedTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                requestedTargets.Add(target);
            }
        }

        return requestedTargets;
    }

    /// <summary>
    /// Adds summary notes.
    /// </summary>
    public static void AddSummaryNotes(DeckPlanSummary summary, DeckIntent? intent)
    {
        int lands = Count(summary.RoleCounts, DeckRoles.Lands);
        int ramp = Count(summary.RoleCounts, DeckRoles.Ramp);
        int draw = Count(summary.RoleCounts, DeckRoles.Draw);
        int interaction = Count(summary.RoleCounts, DeckRoles.Interaction) + Count(summary.RoleCounts, DeckRoles.BoardWipes);
        int landTarget = TargetMinimum(intent, DeckRoles.Lands, 35);
        int rampTarget = TargetMinimum(intent, DeckRoles.Ramp, 8);
        int drawTarget = TargetMinimum(intent, DeckRoles.Draw, 8);
        int interactionTarget = TargetMinimum(intent, DeckRoles.Interaction, 8);

        if (intent is not null)
        {
            summary.IntentNotes.Add("Summary thresholds are using the deck intent stored in the description.");
            if (!string.IsNullOrWhiteSpace(intent.Archetype))
            {
                summary.IntentNotes.Add($"Intent archetype: {intent.Archetype}.");
            }
        }

        if (lands >= landTarget)
        {
            summary.Strengths.Add("Land count looks healthy for Commander.");
        }
        else
        {
            summary.Risks.Add("Land count may be low for a Commander deck.");
        }

        if (ramp >= rampTarget)
        {
            summary.Strengths.Add("Ramp density is in a strong range.");
        }
        else
        {
            summary.Risks.Add("Ramp count may be light.");
        }

        if (draw >= drawTarget)
        {
            summary.Strengths.Add("Card draw appears well represented.");
        }
        else
        {
            summary.Risks.Add("Card draw may need reinforcement.");
        }

        if (interaction < interactionTarget)
        {
            summary.Risks.Add("Interaction and board wipe density may be low.");
        }

        summary.NextSteps.Add("Run deck_analyze_draw_odds for lands, ramp, draw, discard, interaction, and board wipes.");
        summary.NextSteps.Add("Review category counts and card facets before applying category changes.");
    }

    /// <summary>
    /// Reads the minimum target for a role.
    /// </summary>
    public static int TargetMinimum(DeckIntent? intent, string role, int fallback)
    {
        return intent?.Targets.TryGetValue(role, out DeckIntentTarget? target) == true
            ? target.Minimum ?? fallback
            : fallback;
    }

    /// <summary>
    /// Suggests a role for a category.
    /// </summary>
    public static string SuggestRoleForCategory(DeckWorkspace workspace, string category)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in workspace.Cards)
        {
            bool matchesCategory = false;
            foreach (string cardCategory in card.Categories ?? [])
            {
                if (cardCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                {
                    matchesCategory = true;
                    break;
                }
            }

            if (!matchesCategory)
            {
                continue;
            }

            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            AddCount(counts, assignment.PrimaryRole, card.Quantity);
        }

        List<KeyValuePair<string, int>> sortedCounts = counts.ToList();
        sortedCounts.Sort((left, right) => right.Value.CompareTo(left.Value));
        return sortedCounts.Count == 0 ? DeckRoles.Utility : sortedCounts[0].Key;
    }

    /// <summary>
    /// Creates a deck edit plan.
    /// </summary>
    public static DeckEditPlan CreatePlan(DeckWorkspace workspace, string name, string kind)
    {
        return new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = name,
            Kind = kind,
            Persistence = DeckPersistence.For(workspace)
        };
    }

    /// <summary>
    /// Gets a card snapshot safely.
    /// </summary>
    public static CardSnapshot GetSnapshot(DeckCard card)
    {
        return card.Snapshot ?? new CardSnapshot();
    }

    /// <summary>
    /// Copies catalog card facts into a workspace snapshot.
    /// </summary>
    public static void ApplyCardSnapshot(DeckCard card, CardInfo cardInfo)
    {
        card.Snapshot = new CardSnapshot
        {
            ManaCost = cardInfo.ManaCost,
            Layout = cardInfo.Layout,
            TypeLine = cardInfo.TypeLine,
            ManaValue = cardInfo.ManaValue,
            OracleText = cardInfo.OracleText,
            Power = cardInfo.Power,
            Toughness = cardInfo.Toughness,
            Loyalty = cardInfo.Loyalty,
            Defense = cardInfo.Defense,
            ColorIdentity = cardInfo.ColorIdentity.ToList(),
            Set = cardInfo.Set,
            CollectorNumber = cardInfo.CollectorNumber,
            Rarity = cardInfo.Rarity,
            Language = cardInfo.Language,
            ReleasedAt = cardInfo.ReleasedAt,
            ScryfallUri = cardInfo.ScryfallUri,
            SelectedPrintingReason = cardInfo.SelectedPrintingReason,
            PricingMode = cardInfo.PricingMode,
            Provenance = new CardSnapshotProvenance
            {
                Provider = "scryfall",
                ProviderCardId = cardInfo.Id,
                SchemaVersion = 1,
                RefreshedAtUtc = DateTimeOffset.UtcNow,
            },
            EdhrecRank = cardInfo.EdhrecRank,
            Keywords = cardInfo.Keywords.ToList(),
            ProducedMana = cardInfo.ProducedMana.ToList(),
            Games = cardInfo.Games.ToList(),
            Finishes = cardInfo.Finishes.ToList(),
            Faces = cardInfo.Faces.Select(CloneFace).ToList(),
            Legalities = new Dictionary<string, string>(cardInfo.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(cardInfo.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(cardInfo.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Copies cached snapshot facts without sharing mutable collections.
    /// </summary>
    public static CardSnapshot CopyCardSnapshot(CardSnapshot snapshot)
    {
        return new CardSnapshot
        {
            ManaCost = snapshot.ManaCost,
            Layout = snapshot.Layout,
            TypeLine = snapshot.TypeLine,
            ManaValue = snapshot.ManaValue,
            OracleText = snapshot.OracleText,
            Power = snapshot.Power,
            Toughness = snapshot.Toughness,
            Loyalty = snapshot.Loyalty,
            Defense = snapshot.Defense,
            ColorIdentity = snapshot.ColorIdentity.ToList(),
            Set = snapshot.Set,
            CollectorNumber = snapshot.CollectorNumber,
            Rarity = snapshot.Rarity,
            Language = snapshot.Language,
            ReleasedAt = snapshot.ReleasedAt,
            ScryfallUri = snapshot.ScryfallUri,
            SelectedPrintingReason = snapshot.SelectedPrintingReason,
            PricingMode = snapshot.PricingMode,
            Provenance = new CardSnapshotProvenance
            {
                Provider = snapshot.Provenance.Provider,
                ProviderCardId = snapshot.Provenance.ProviderCardId,
                SchemaVersion = snapshot.Provenance.SchemaVersion,
                RefreshedAtUtc = snapshot.Provenance.RefreshedAtUtc,
            },
            EdhrecRank = snapshot.EdhrecRank,
            Keywords = snapshot.Keywords.ToList(),
            ProducedMana = snapshot.ProducedMana.ToList(),
            Games = snapshot.Games.ToList(),
            Finishes = snapshot.Finishes.ToList(),
            Faces = snapshot.Faces.Select(CloneFace).ToList(),
            Legalities = new Dictionary<string, string>(snapshot.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(snapshot.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(snapshot.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Copies one face snapshot without sharing mutable color lists.
    /// </summary>
    public static CardFaceSnapshot CloneFace(CardFaceSnapshot face)
    {
        return new CardFaceSnapshot
        {
            Name = face.Name,
            ManaCost = face.ManaCost,
            TypeLine = face.TypeLine,
            OracleText = face.OracleText,
            Power = face.Power,
            Toughness = face.Toughness,
            Loyalty = face.Loyalty,
            Defense = face.Defense,
            Colors = face.Colors.ToList(),
        };
    }

    /// <summary>
    /// Builds a bounded quality summary from current card snapshots.
    /// </summary>
    public static CardSnapshotQualitySummary BuildSnapshotQualitySummary(IReadOnlyList<DeckCard> cards)
    {
        CardSnapshotQualitySummary summary = new()
        {
            CardCount = cards.Count
        };
        foreach (DeckCard card in cards)
        {
            CardSnapshot? snapshot = card.Snapshot;
            if (snapshot is null)
            {
                continue;
            }

            summary.SnapshotCount++;
            bool hasTypeLine = !string.IsNullOrWhiteSpace(snapshot.TypeLine);
            bool hasOracleText = !string.IsNullOrWhiteSpace(snapshot.OracleText);
            bool hasPrices = snapshot.Prices.Count > 0;
            bool hasProducedMana = snapshot.ProducedMana.Count > 0;
            if (hasTypeLine)
            {
                summary.TypeLineCount++;
            }

            if (hasOracleText)
            {
                summary.OracleTextCount++;
            }

            if (hasPrices)
            {
                summary.PriceCount++;
            }

            if (hasProducedMana)
            {
                summary.ProducedManaCount++;
            }

            if (hasTypeLine && (hasOracleText || hasPrices || hasProducedMana))
            {
                summary.AnalysisReadyCount++;
            }
        }

        return summary;
    }

    /// <summary>
    /// Adds a quantity to a count dictionary.
    /// </summary>
    public static void AddCount(Dictionary<string, int> counts, string key, int quantity)
    {
        counts[key] = counts.GetValueOrDefault(key) + Math.Max(0, quantity);
    }

    /// <summary>
    /// Gets a count value.
    /// </summary>
    public static int Count(Dictionary<string, int> counts, string key)
    {
        return counts.TryGetValue(key, out int count) ? count : 0;
    }

    /// <summary>
    /// Requires an operation value.
    /// </summary>
    public static string Require(string? value, string name)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Deck edit operation is missing required field '{name}'.");
    }

    /// <summary>
    /// Requires the plan Repository.
    /// </summary>
    public static IDeckPlanRepository RequirePlanRepository(IDeckPlanRepository? planRepository)
    {
        return planRepository ?? throw new InvalidOperationException("Deck edit plan persistence is not configured.");
    }

    /// <summary>
    /// Requires Archidekt support for operations that cannot run against local workspaces only.
    /// </summary>
    public static IArchidektGateway RequireArchidektGateway(IArchidektGateway? archidektGateway)
    {
        return archidektGateway
            ?? throw new InvalidOperationException("Archidekt support is not configured.");
    }

    /// <summary>
    /// Captures enough card metadata to distinguish real refresh changes.
    /// </summary>
    private sealed record CardSnapshotFingerprint(
        string? ScryfallId,
        string? ScryfallOracleId,
        string? ManaCost,
        double? ManaValue,
        string? TypeLine,
        string? OracleText,
        string? Layout,
        string? Power,
        string? Toughness,
        string? Loyalty,
        string? Defense,
        string? Set,
        string? CollectorNumber,
        string? Rarity,
        string? Language,
        DateOnly? ReleasedAt,
        string? ScryfallUri,
        string? SelectedPrintingReason,
        string? PricingMode,
        int? EdhrecRank,
        string Provenance,
        string ColorIdentity,
        string Keywords,
        string ProducedMana,
        string Games,
        string Finishes,
        string Faces,
        string Legalities,
        string Prices,
        string ImageUris)
    {
        /// <summary>
        /// Captures the current snapshot state for comparison.
        /// </summary>
        public static CardSnapshotFingerprint From(DeckCard card)
        {
            CardSnapshot snapshot = GetSnapshot(card);
            return new CardSnapshotFingerprint(
                card.ScryfallId,
                card.ScryfallOracleId,
                snapshot.ManaCost,
                snapshot.ManaValue,
                snapshot.TypeLine,
                snapshot.OracleText,
                snapshot.Layout,
                snapshot.Power,
                snapshot.Toughness,
                snapshot.Loyalty,
                snapshot.Defense,
                snapshot.Set,
                snapshot.CollectorNumber,
                snapshot.Rarity,
                snapshot.Language,
                snapshot.ReleasedAt,
                snapshot.ScryfallUri,
                snapshot.SelectedPrintingReason,
                snapshot.PricingMode,
                snapshot.EdhrecRank,
                Joined(snapshot.Provenance),
                Joined(snapshot.ColorIdentity),
                Joined(snapshot.Keywords),
                Joined(snapshot.ProducedMana),
                Joined(snapshot.Games),
                Joined(snapshot.Finishes),
                Joined(snapshot.Faces),
                Joined(snapshot.Legalities),
                Joined(snapshot.Prices),
                Joined(snapshot.ImageUris));
        }

        /// <summary>
        /// Joins provenance fields that change only when source identity or schema changes.
        /// </summary>
        private static string Joined(CardSnapshotProvenance provenance)
        {
            return string.Join(
                '\u001f',
                provenance.Provider,
                provenance.ProviderCardId,
                provenance.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Joins face values in stable source order.
        /// </summary>
        private static string Joined(IReadOnlyList<CardFaceSnapshot> faces)
        {
            List<string> values = [];
            foreach (CardFaceSnapshot face in faces)
            {
                values.Add(string.Join(
                    '\u001e',
                    face.Name,
                    face.ManaCost,
                    face.TypeLine,
                    face.OracleText,
                    face.Power,
                    face.Toughness,
                    face.Loyalty,
                    face.Defense,
                    Joined(face.Colors)));
            }

            return string.Join('\u001f', values);
        }

        /// <summary>
        /// Joins list values in stable order.
        /// </summary>
        private static string Joined(IReadOnlyList<string> values)
        {
            List<string> sorted = values.ToList();
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join('\u001f', sorted);
        }

        /// <summary>
        /// Joins dictionary values in stable order.
        /// </summary>
        private static string Joined(IReadOnlyDictionary<string, string> values)
        {
            List<string> pairs = [];
            foreach (KeyValuePair<string, string> value in values)
            {
                pairs.Add($"{value.Key}\u001e{value.Value}");
            }

            pairs.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join('\u001f', pairs);
        }
    }
}
