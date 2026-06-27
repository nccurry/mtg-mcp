namespace MtgMcp.Core;

/// <summary>
/// Builds evidence rows for weak-slot review workflows.
/// </summary>
public sealed partial class DeckAnalysisService
{
    /// <summary>
    /// Returns deterministic weak-slot evidence, role balance, and existing candidate rows without final cut decisions.
    /// </summary>
    public async Task<DeckWeakSpotReview> ReviewWeakSpotsAsync(
        string workspaceId,
        string analysisProfile,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return ReviewWeakSpotsSnapshot(workspace, analysisProfile, limit);
    }

    /// <summary>
    /// Returns deterministic weak-slot evidence for an in-memory workspace snapshot.
    /// </summary>
    public DeckWeakSpotReview ReviewWeakSpotsSnapshot(
        DeckWorkspace workspace,
        string analysisProfile,
        int limit)
    {
        DeckBestPracticeAnalysis bestPractices = AnalyzeDeckBestPracticesSnapshot(workspace, analysisProfile);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        DeckWorkspaceState state = DeckWorkspaceService.BuildWorkspaceState(workspace);
        DeckWeakSpotReview review = new()
        {
            WorkspaceId = workspace.Id,
            State = state
        };

        AddBalanceRows(review, bestPractices);
        AddCategoryRows(review, workspace, state);
        AddWeakSlotRows(review, workspace, bestPractices, intent, Math.Clamp(limit, 1, 100));
        AddCandidateRows(review, workspace, bestPractices, Math.Clamp(limit, 1, 100));
        review.SourceStatuses.Add(new DeckWeakSpotSourceStatus
        {
            SourceKey = "workspace",
            Status = "evaluated",
            Notes = ["Used saved workspace cards, categories, cached Scryfall snapshots, validation, and heuristic role classifiers."]
        });
        review.SourceStatuses.Add(new DeckWeakSpotSourceStatus
        {
            SourceKey = "external-recommendation-sources",
            Status = "not-queried",
            Notes =
            [
                "Call source_search_evidence or commander_get_aggregate_cards "
                    + "when source-backed popularity evidence is needed."
            ]
        });
        review.Notes.Add(
            "Evidence-only review: rows identify pressure points and candidates, "
                + "but the assistant should synthesize final cuts and replacements.");
        review.Notes.Add($"Using best-practice profile {bestPractices.RecommendedProfile}.");
        return review;
    }

    /// <summary>
    /// Adds role and tag balance rows from best-practice analysis.
    /// </summary>
    private static void AddBalanceRows(DeckWeakSpotReview review, DeckBestPracticeAnalysis bestPractices)
    {
        foreach (DeckNeed need in bestPractices.NeedProfile.RoleNeeds)
        {
            review.RoleBalance.Add(BalanceRow(need, "role"));
        }

        foreach (DeckNeed need in bestPractices.NeedProfile.TagNeeds)
        {
            review.RoleBalance.Add(BalanceRow(need, "tag"));
        }

        review.RoleBalance.Sort(CompareBalanceRows);
    }

    /// <summary>
    /// Builds one role or tag balance row.
    /// </summary>
    private static DeckWeakSpotBalanceRow BalanceRow(DeckNeed need, string kind)
    {
        return new DeckWeakSpotBalanceRow
        {
            Target = need.Target,
            TargetKind = kind,
            CurrentCount = need.CurrentCount,
            Minimum = need.Minimum,
            Maximum = need.Maximum,
            Status = need.Status,
            Rationale = need.Rationale
        };
    }

    /// <summary>
    /// Adds category count and inclusion evidence.
    /// </summary>
    private static void AddCategoryRows(
        DeckWeakSpotReview review,
        DeckWorkspace workspace,
        DeckWorkspaceState state)
    {
        foreach (DeckCategory category in workspace.Categories)
        {
            state.CategoryCounts.TryGetValue(category.Name, out int count);
            DeckWeakSpotCategoryRow row = new()
            {
                Category = category.Name,
                Count = count,
                IncludedInDeck = category.IncludedInDeck
            };

            if (!category.IncludedInDeck && count > 0)
            {
                row.Signals.Add("Excluded category contains cards that may be candidates or parking-lot items.");
            }

            if (category.IncludedInDeck && count == 0)
            {
                row.Signals.Add("Included category is empty.");
            }

            if (category.IncludedInDeck && count > 35 && !category.Name.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            {
                row.Signals.Add("Large included category may hide role imbalance; review category organization.");
            }

            review.CategoryBalance.Add(row);
        }

        review.CategoryBalance.Sort((left, right) =>
            string.Compare(left.Category, right.Category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds active card rows with deterministic weak-slot signals.
    /// </summary>
    private static void AddWeakSlotRows(
        DeckWeakSpotReview review,
        DeckWorkspace workspace,
        DeckBestPracticeAnalysis bestPractices,
        DeckIntent? intent,
        int limit)
    {
        HashSet<string> highTargets = TargetsWithStatus(bestPractices, "high");
        foreach (DeckCard card in workspace.Cards)
        {
            if (!DeckCategoryInclusion.IsIncludedInDeck(workspace, card))
            {
                continue;
            }

            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
            DeckWeakSlotEvidenceRow row = new()
            {
                CardName = card.Name,
                Quantity = card.Quantity,
                PrimaryCategory = DeckCategoryOrdering.PrimaryCategory(card),
                Role = assignment.PrimaryRole,
                Tags = assignment.Tags.ToList(),
                ManaValue = snapshot.ManaValue,
                Price = ReadUsdPrice(snapshot),
                ClassifierConfidence = assignment.Confidence,
                ScryfallUri = snapshot.ScryfallUri,
                ProtectedCardWarnings = DeckIntentProtection.IsProtectedCard(card, intent)
                    ? ["Card is protected by deck intent."]
                    : []
            };

            AddWeakSignals(row, workspace, card, assignment, highTargets);
            if (row.Signals.Count > 0)
            {
                review.WeakSlots.Add(row);
            }
        }

        review.WeakSlots.Sort(CompareWeakRows);
        if (review.WeakSlots.Count > limit)
        {
            review.WeakSlots.RemoveRange(limit, review.WeakSlots.Count - limit);
        }
    }

    /// <summary>
    /// Adds evidence signals for one active card.
    /// </summary>
    private static void AddWeakSignals(
        DeckWeakSlotEvidenceRow row,
        DeckWorkspace workspace,
        DeckCard card,
        CardRoleAssignment assignment,
        HashSet<string> highTargets)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        bool finisher = assignment.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
            || assignment.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase);
        bool commander = assignment.PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase);
        bool land = assignment.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(snapshot.TypeLine)
            && string.IsNullOrWhiteSpace(snapshot.OracleText)
            && string.IsNullOrWhiteSpace(card.ScryfallId))
        {
            row.Signals.Add("Missing cached Scryfall snapshot; refresh metadata before making a final call.");
        }

        if (snapshot.ManaValue >= 6 && !finisher && !commander)
        {
            row.Signals.Add("High mana value without a finisher or commander role signal.");
        }

        if (assignment.Confidence < 0.55 && !commander)
        {
            row.Signals.Add("Low classifier confidence; category or tags may need correction.");
        }

        if (highTargets.Contains(assignment.PrimaryRole))
        {
            row.Signals.Add($"Role {assignment.PrimaryRole} is above the selected target band.");
        }

        foreach (string tag in assignment.Tags)
        {
            if (highTargets.Contains(tag))
            {
                row.Signals.Add($"Tag {tag} is above the selected target band.");
            }
        }

        if (row.Price is >= 10m && !commander && !finisher)
        {
            row.Signals.Add("High-price active card without finisher or commander signal; compare against deck budget intent.");
        }

        if (workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase)
            && card.Quantity > 1
            && !land
            && !IsBasicLand(snapshot.TypeLine))
        {
            row.Signals.Add("Multiple copies in a Commander workspace; validation may also report this.");
        }
    }

    /// <summary>
    /// Adds existing excluded cards that match low role or tag targets.
    /// </summary>
    private static void AddCandidateRows(
        DeckWeakSpotReview review,
        DeckWorkspace workspace,
        DeckBestPracticeAnalysis bestPractices,
        int limit)
    {
        List<DeckWeakSpotBalanceRow> lowTargets = review.RoleBalance
            .Where(row => row.Status.Equals("low", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (DeckCard card in workspace.Cards)
        {
            if (DeckCategoryInclusion.IsIncludedInDeck(workspace, card))
            {
                continue;
            }

            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            bool matchedAny = false;
            foreach (DeckWeakSpotBalanceRow target in lowTargets)
            {
                bool matched = target.TargetKind.Equals("role", StringComparison.OrdinalIgnoreCase)
                    ? assignment.PrimaryRole.Equals(target.Target, StringComparison.OrdinalIgnoreCase)
                    : assignment.Tags.Contains(target.Target, StringComparer.OrdinalIgnoreCase);
                if (!matched)
                {
                    continue;
                }

                CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
                review.CandidateRows.Add(new DeckWeakSpotCandidateRow
                {
                    CardName = card.Name,
                    SourceCategory = DeckCategoryOrdering.PrimaryCategory(card),
                    MatchedTarget = target.Target,
                    TargetKind = target.TargetKind,
                    Price = ReadUsdPrice(snapshot),
                    ScryfallUri = snapshot.ScryfallUri,
                    Rationale = $"Excluded card matches low {target.TargetKind} target {target.Target}."
                });
                matchedAny = true;
            }

            if (!matchedAny
                && TryBuildFallbackCandidate(card, assignment, out DeckWeakSpotCandidateRow? fallback)
                && fallback is not null)
            {
                review.CandidateRows.Add(fallback);
            }
        }

        review.CandidateRows.Sort((left, right) =>
        {
            int target = string.Compare(left.MatchedTarget, right.MatchedTarget, StringComparison.OrdinalIgnoreCase);
            return target != 0
                ? target
                : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
        });
        if (review.CandidateRows.Count > limit)
        {
            review.CandidateRows.RemoveRange(limit, review.CandidateRows.Count - limit);
        }
    }

    /// <summary>
    /// Builds a candidate row for an excluded card that has useful local role or category evidence.
    /// </summary>
    private static bool TryBuildFallbackCandidate(
        DeckCard card,
        CardRoleAssignment assignment,
        out DeckWeakSpotCandidateRow? row)
    {
        row = null;
        string target = FirstCandidateTarget(card, assignment);
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        row = new DeckWeakSpotCandidateRow
        {
            CardName = card.Name,
            SourceCategory = DeckCategoryOrdering.PrimaryCategory(card),
            MatchedTarget = target,
            TargetKind = "local-evidence",
            Price = ReadUsdPrice(snapshot),
            ScryfallUri = snapshot.ScryfallUri,
            Rationale = $"Excluded card has local role/category evidence for {target}."
        };
        return true;
    }

    /// <summary>
    /// Finds the most useful role or category evidence label for an excluded candidate.
    /// </summary>
    private static string FirstCandidateTarget(DeckCard card, CardRoleAssignment assignment)
    {
        foreach (string category in DeckCategoryOrdering.OrderedDistinct(
                DeckCategoryOrdering.PrimaryCategory(card),
                card.Categories)
            .Skip(1))
        {
            if (!IsParkingCategory(category))
            {
                return category;
            }
        }

        if (!IsParkingCategory(assignment.PrimaryRole)
            && !assignment.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            && !assignment.PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase))
        {
            return assignment.PrimaryRole;
        }

        return assignment.Tags.FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Checks whether a category is only a parking zone rather than role evidence.
    /// </summary>
    private static bool IsParkingCategory(string category)
    {
        return category.Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase)
            || category.Equals(DeckDefaults.Sideboard, StringComparison.OrdinalIgnoreCase)
            || category.Equals(DeckDefaults.Considering, StringComparison.OrdinalIgnoreCase)
            || category.Equals(DeckRoles.Maybeboard, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a set of high-count role and tag targets.
    /// </summary>
    private static HashSet<string> TargetsWithStatus(DeckBestPracticeAnalysis bestPractices, string status)
    {
        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckNeed need in bestPractices.NeedProfile.RoleNeeds)
        {
            if (need.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            {
                targets.Add(need.Target);
            }
        }

        foreach (DeckNeed need in bestPractices.NeedProfile.TagNeeds)
        {
            if (need.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            {
                targets.Add(need.Target);
            }
        }

        return targets;
    }

    /// <summary>
    /// Checks a type line for a basic land subtype.
    /// </summary>
    private static bool IsBasicLand(string? typeLine)
    {
        return !string.IsNullOrWhiteSpace(typeLine)
            && typeLine.Contains("Basic", StringComparison.OrdinalIgnoreCase)
            && typeLine.Contains("Land", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorts balance rows by status severity, target kind, then target.
    /// </summary>
    private static int CompareBalanceRows(
        DeckWeakSpotBalanceRow left,
        DeckWeakSpotBalanceRow right)
    {
        int severity = BalanceSeverity(right.Status).CompareTo(BalanceSeverity(left.Status));
        if (severity != 0)
        {
            return severity;
        }

        int kind = string.Compare(left.TargetKind, right.TargetKind, StringComparison.OrdinalIgnoreCase);
        return kind != 0
            ? kind
            : string.Compare(left.Target, right.Target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scores balance statuses for sorting.
    /// </summary>
    private static int BalanceSeverity(string status)
    {
        return status.Equals("low", StringComparison.OrdinalIgnoreCase)
            || status.Equals("high", StringComparison.OrdinalIgnoreCase)
            ? 2
            : status.Equals("ok", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
    }

    /// <summary>
    /// Sorts weak rows by signal count, mana value, price, and name.
    /// </summary>
    private static int CompareWeakRows(
        DeckWeakSlotEvidenceRow left,
        DeckWeakSlotEvidenceRow right)
    {
        int signals = right.Signals.Count.CompareTo(left.Signals.Count);
        if (signals != 0)
        {
            return signals;
        }

        int protectedWarnings = left.ProtectedCardWarnings.Count.CompareTo(right.ProtectedCardWarnings.Count);
        if (protectedWarnings != 0)
        {
            return protectedWarnings;
        }

        int manaValue = Nullable.Compare(right.ManaValue, left.ManaValue);
        if (manaValue != 0)
        {
            return manaValue;
        }

        int price = Nullable.Compare(right.Price, left.Price);
        return price != 0
            ? price
            : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }
}
