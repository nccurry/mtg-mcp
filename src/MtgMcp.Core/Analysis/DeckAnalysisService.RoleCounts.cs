namespace MtgMcp.Core;

/// <summary>
/// Explains role-count evidence for workspace cards.
/// </summary>
public sealed partial class DeckAnalysisService
{
    /// <summary>
    /// Explains why category, classifier, and draw-odds counts include cards for a role-like target.
    /// </summary>
    public async Task<DeckRoleCountExplanation> ExplainRoleCountsAsync(
        string workspaceId,
        string role,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("A role, tag, or category target is required.", nameof(role));
        }

        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string target = role.Trim();
        DeckRoleCountExplanation result = new()
        {
            WorkspaceId = workspace.Id,
            Role = target
        };

        foreach (DeckCard card in workspace.Cards)
        {
            DeckRoleCountCardEvidence evidence = BuildRoleCountCardEvidence(workspace, card, target);
            if (evidence.IncludedInDeck)
            {
                if (evidence.CountedByCategory)
                {
                    result.CategoryCount += Math.Max(0, card.Quantity);
                }

                if (evidence.CountedByAnyCategory)
                {
                    result.AllCategoryCount += Math.Max(0, card.Quantity);
                }

                if (evidence.CountedByHeuristic)
                {
                    result.HeuristicCount += Math.Max(0, card.Quantity);
                }

                if (evidence.CountedByFunctionalRole)
                {
                    result.FunctionalCount += Math.Max(0, card.Quantity);
                }

                if (evidence.CountedByOddsTarget)
                {
                    result.OddsTargetCount += Math.Max(0, card.Quantity);
                }
            }

            if (evidence.MatchingEvidence.Count > 0)
            {
                result.Cards.Add(evidence);
            }
        }

        result.Cards.Sort(CompareRoleEvidenceRows);
        AddRoleCountNotes(result);
        return result;
    }

    /// <summary>
    /// Builds one card's role-count evidence row.
    /// </summary>
    private static DeckRoleCountCardEvidence BuildRoleCountCardEvidence(
        DeckWorkspace workspace,
        DeckCard card,
        string target)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
        bool included = DeckCategoryInclusion.IsIncludedInDeck(workspace, card);
        bool categoryMatch = primaryCategory.Equals(target, StringComparison.OrdinalIgnoreCase);
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        List<string> categories = DeckCategoryOrdering.OrderedDistinct(primaryCategory, card.Categories).ToList();
        bool anyCategoryMatch = categories.Any(category => category.Equals(target, StringComparison.OrdinalIgnoreCase));
        bool heuristicMatch = assignment.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase);
        bool functionalMatch = assignment.FunctionalRoles.Any(role => role.Equals(target, StringComparison.OrdinalIgnoreCase));
        bool oddsMatch = DeckRoleClassifier.MatchesTarget(card, target);
        DeckRoleCountCardEvidence evidence = new()
        {
            CardName = card.Name,
            Quantity = card.Quantity,
            PrimaryCategory = primaryCategory,
            Categories = categories,
            IncludedInDeck = included,
            ClassifierPrimaryRole = assignment.PrimaryRole,
            Tags = assignment.Tags.ToList(),
            FunctionalRoles = assignment.FunctionalRoles.ToList(),
            ClassifierConfidence = assignment.Confidence,
            CountedByCategory = included && categoryMatch,
            CountedByAnyCategory = included && anyCategoryMatch,
            CountedByHeuristic = included && heuristicMatch,
            CountedByFunctionalRole = included && functionalMatch,
            CountedByOddsTarget = included && oddsMatch,
            TypeLine = snapshot.TypeLine,
            OracleSnippet = Snippet(snapshot.OracleText),
            ScryfallUri = snapshot.ScryfallUri
        };

        AddRoleEvidence(evidence.MatchingEvidence, "primary category", primaryCategory, categoryMatch);
        AddRoleEvidence(evidence.MatchingEvidence, "classifier primary role", assignment.PrimaryRole, heuristicMatch);
        foreach (string tag in assignment.Tags)
        {
            AddRoleEvidence(
                evidence.MatchingEvidence,
                "classifier tag",
                tag,
                tag.Equals(target, StringComparison.OrdinalIgnoreCase));
        }

        foreach (string functionalRole in assignment.FunctionalRoles)
        {
            AddRoleEvidence(
                evidence.MatchingEvidence,
                "classifier functional role",
                functionalRole,
                functionalRole.Equals(target, StringComparison.OrdinalIgnoreCase));
        }

        foreach (string category in categories)
        {
            AddRoleEvidence(
                evidence.MatchingEvidence,
                "workspace category",
                category,
                category.Equals(target, StringComparison.OrdinalIgnoreCase));
        }

        AddAnnotationEvidence(evidence.MatchingEvidence, card, target, CardFacetNames.UserTags, "user tag");
        AddAnnotationEvidence(evidence.MatchingEvidence, card, target, CardFacetNames.UserCategories, "user category");
        AddAnnotationEvidence(evidence.MatchingEvidence, card, target, CardFacetNames.TaggerOracleTags, "tagger oracle tag");
        AddOracleEvidence(evidence.MatchingEvidence, snapshot.OracleText, target);
        AddClassifierOracleEvidence(evidence.MatchingEvidence, snapshot.OracleText, target, heuristicMatch);
        if (!included && evidence.MatchingEvidence.Count > 0)
        {
            evidence.MatchingEvidence.Add("Card matched evidence but is excluded by its primary category.");
        }

        return evidence;
    }

    /// <summary>
    /// Adds divergence and data-quality notes to a role explanation.
    /// </summary>
    private static void AddRoleCountNotes(DeckRoleCountExplanation result)
    {
        if (result.CategoryCount != result.AllCategoryCount
            || result.AllCategoryCount != result.HeuristicCount
            || result.HeuristicCount != result.FunctionalCount
            || result.FunctionalCount != result.OddsTargetCount)
        {
            result.Notes.Add(
                $"Counts diverge: primary-category={result.CategoryCount}, all-categories={result.AllCategoryCount}, heuristic={result.HeuristicCount}, functional={result.FunctionalCount}, odds-target={result.OddsTargetCount}.");
        }

        if (result.AllCategoryCount > result.CategoryCount)
        {
            result.Notes.Add("All-category counts include secondary categories, while primary-category counts preserve the card's main composition bucket.");
        }

        if (result.OddsTargetCount > result.HeuristicCount)
        {
            result.Notes.Add("Odds target matching includes functional roles, secondary tags, and category labels, so it can be broader than classifier primary role.");
        }

        if (result.Cards.Count == 0)
        {
            result.Notes.Add("No workspace cards matched the requested role, tag, or category evidence.");
        }
    }

    /// <summary>
    /// Adds one role evidence line when a concrete value matches.
    /// </summary>
    private static void AddRoleEvidence(
        List<string> evidence,
        string source,
        string value,
        bool matched)
    {
        if (matched)
        {
            evidence.Add($"{source}: {value}");
        }
    }

    /// <summary>
    /// Adds annotation evidence from card metadata.
    /// </summary>
    private static void AddAnnotationEvidence(
        List<string> evidence,
        DeckCard card,
        string target,
        string metadataKey,
        string label)
    {
        if (!card.Metadata.TryGetValue(metadataKey, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (string part in SplitAnnotationValues(value))
        {
            if (part.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                evidence.Add($"{label}: {part}");
            }
        }
    }

    /// <summary>
    /// Adds a short oracle-text evidence note when the requested target appears in rules text.
    /// </summary>
    private static void AddOracleEvidence(List<string> evidence, string? oracleText, string target)
    {
        if (string.IsNullOrWhiteSpace(oracleText)
            || !ContainsWholeTextToken(oracleText, target))
        {
            return;
        }

        evidence.Add($"oracle text contains '{target}'");
    }

    /// <summary>
    /// Adds role-specific oracle clues when classifier text heuristics caused the role match.
    /// </summary>
    private static void AddClassifierOracleEvidence(
        List<string> evidence,
        string? oracleText,
        string target,
        bool heuristicMatch)
    {
        if (!heuristicMatch || string.IsNullOrWhiteSpace(oracleText))
        {
            return;
        }

        if (target.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
            && ContainsAnyPhrase(oracleText, "add {", "add one mana", "add two", "treasure token", "search your library for a land"))
        {
            evidence.Add("oracle text supports Ramp through mana production or land search.");
            return;
        }

        if (target.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase)
            && ContainsAnyPhrase(oracleText, "draw a card", "draw two", "draw three", "draw cards", "draw that many"))
        {
            evidence.Add("oracle text supports Draw through card-draw text.");
            return;
        }

        if (target.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            && ContainsAnyPhrase(oracleText, "destroy target", "exile target", "counter target", "return target", "fight target"))
        {
            evidence.Add("oracle text supports Interaction through removal, counterspell, bounce, or fight text.");
            return;
        }

        if (target.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase)
            && ContainsAnyPhrase(
                oracleText,
                "destroy all",
                "exile all",
                "all creatures get -",
                "each creature gets -",
                "damage to each creature"))
        {
            evidence.Add("oracle text supports Board Wipes through broad destructive or reset text.");
            return;
        }

        if (target.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase)
            && ContainsAnyPhrase(oracleText, "hexproof", "indestructible", "protection from", "phase out", "can't be countered"))
        {
            evidence.Add("oracle text supports Protection through protection or resilience text.");
            return;
        }

        if (target.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
            && ContainsAnyPhrase(oracleText, "you win the game", "creatures you control get", "each opponent loses", "trample until end of turn"))
        {
            evidence.Add("oracle text supports Wincons through lethal, pump, drain, or alternate-win text.");
        }
    }

    /// <summary>
    /// Checks whether a target appears as its own word or phrase in rules text.
    /// </summary>
    private static bool ContainsWholeTextToken(string text, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        int start = 0;
        while (start < text.Length)
        {
            int index = text.IndexOf(target, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            int end = index + target.Length;
            bool startsClean = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            bool endsClean = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsClean && endsClean)
            {
                return true;
            }

            start = end;
        }

        return false;
    }

    /// <summary>
    /// Checks whether text contains any supplied phrase.
    /// </summary>
    private static bool ContainsAnyPhrase(string text, params string[] phrases)
    {
        foreach (string phrase in phrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits stored annotation values using facet-compatible separators.
    /// </summary>
    private static IEnumerable<string> SplitAnnotationValues(string value)
    {
        return value.Split(
            [',', ';', '|', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Creates a short single-line oracle text excerpt.
    /// </summary>
    private static string? Snippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 180 ? singleLine : string.Concat(singleLine.AsSpan(0, 177), "...");
    }

    /// <summary>
    /// Sorts role evidence rows by active status, match strength, and card name.
    /// </summary>
    private static int CompareRoleEvidenceRows(
        DeckRoleCountCardEvidence left,
        DeckRoleCountCardEvidence right)
    {
        int included = right.IncludedInDeck.CompareTo(left.IncludedInDeck);
        if (included != 0)
        {
            return included;
        }

        int matchCount = MatchScore(right).CompareTo(MatchScore(left));
        return matchCount != 0
            ? matchCount
            : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scores a row by how many count paths matched.
    /// </summary>
    private static int MatchScore(DeckRoleCountCardEvidence evidence)
    {
        int score = 0;
        if (evidence.CountedByCategory)
        {
            score++;
        }

        if (evidence.CountedByAnyCategory)
        {
            score++;
        }

        if (evidence.CountedByHeuristic)
        {
            score++;
        }

        if (evidence.CountedByFunctionalRole)
        {
            score++;
        }

        if (evidence.CountedByOddsTarget)
        {
            score++;
        }

        return score;
    }
}
