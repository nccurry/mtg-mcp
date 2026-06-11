namespace MtgMcp.Core;

/// <summary>
/// Provides best-practice deck analysis behavior.
/// </summary>
public sealed partial class DeckAnalysisService : DeckServiceBase
{
    /// <summary>
    /// Analyzes a deck against common Commander construction heuristics.
    /// </summary>
    public async Task<DeckBestPracticeAnalysis> AnalyzeDeckBestPracticesAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        return await AnalyzeDeckBestPracticesAsync(workspaceId, "auto", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Analyzes a deck against common Commander construction heuristics.
    /// </summary>
    public async Task<DeckBestPracticeAnalysis> AnalyzeDeckBestPracticesAsync(
        string workspaceId,
        string profile,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        List<DeckHeuristicProfile> profiles = DeckHeuristicProfileCatalog.BuiltIns();
        BestPracticeProfileResolution profileResolution = ResolveBestPracticeProfile(profile, intent);
        string selectedProfileId = profileResolution.ProfileId;

        DeckHeuristicProfile selectedProfile = profiles.FirstOrDefault(value => value.Id.Equals(selectedProfileId, StringComparison.OrdinalIgnoreCase))
            ?? profiles[0];
        DeckNeedProfile needProfile = BuildNeedProfile(workspace, intent, selectedProfile);
        ManaBaseAnalysis mana = AnalyzeManaBase(workspace);
        DeckConsistencyAnalysis consistency = AnalyzeDeckConsistency(workspace);
        DeckAnalysis deck = DeckAnalyzer.Analyze(workspace);
        DeckBestPracticeAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            ConfigVersion = DeckHeuristicProfileCatalog.BuiltInConfigVersion,
            NeedProfile = needProfile,
            RecommendedProfile = selectedProfile.Id,
            ProfileSource = profileResolution.Source,
            HeuristicComparisons = profiles
                .Select(value => CompareHeuristicProfile(deck, intent, value))
                .OrderByDescending(value => value.FitScore)
                .ToList(),
            Citations = BuildBestPracticeCitations()
        };

        foreach (DeckNeed need in needProfile.RoleNeeds.Concat(needProfile.TagNeeds))
        {
            if (need.Status.Equals("low", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Risks.Add(need.Rationale);
                analysis.Recommendations.Add($"Increase {need.Target} density toward {need.Minimum} cards.");
            }
            else if (need.Status.Equals("high", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Risks.Add(need.Rationale);
                analysis.Recommendations.Add($"Review whether {need.Target} is crowding out the deck's core plan.");
            }
            else
            {
                analysis.Strengths.Add($"{need.Target} density is within the target band.");
            }
        }

        analysis.Risks.AddRange(mana.Risks);
        analysis.Risks.AddRange(consistency.Risks);
        if (Count(deck.RoleCounts, DeckRoles.Wincons) + Count(deck.TagCounts, DeckTags.Finishers) == 0)
        {
            analysis.Risks.Add("No clear win condition or finisher package was detected.");
            analysis.Recommendations.Add("Add or tag finishers so win-turn projection can identify the deck's closing plan.");
        }

        if (Count(deck.RoleCounts, DeckRoles.Interaction) + Count(deck.RoleCounts, DeckRoles.BoardWipes) >= 8)
        {
            analysis.Strengths.Add("Interaction coverage appears reasonable for a Commander deck.");
        }

        analysis.Recommendations = analysis.Recommendations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        if (profileResolution.Source.Equals("baseline-default", StringComparison.OrdinalIgnoreCase))
        {
            analysis.Strengths.Add("No explicit archetype heuristic profile was selected; using the Commander baseline.");
        }

        return analysis;
    }

    /// <summary>
    /// Builds need rows from role and tag counts.
    /// </summary>
    private static DeckNeedProfile BuildNeedProfile(
        DeckWorkspace workspace,
        DeckIntent? intent,
        DeckHeuristicProfile heuristicProfile)
    {
        DeckAnalysis analysis = DeckAnalyzer.Analyze(workspace);
        DeckNeedProfile profile = new()
        {
            WorkspaceId = workspace.Id,
            Format = workspace.Format
        };
        Dictionary<string, (int Minimum, int? Maximum)> roleTargets = ApplyIntentTargets(heuristicProfile.RoleTargets, intent);
        Dictionary<string, (int Minimum, int? Maximum)> tagTargets = ApplyIntentTargets(heuristicProfile.TagTargets, intent);

        foreach ((string target, (int minimum, int? maximum)) in roleTargets)
        {
            profile.RoleNeeds.Add(BuildNeed(target, CountNeedTarget(analysis, target), minimum, maximum));
        }

        foreach ((string target, (int minimum, int? maximum)) in tagTargets)
        {
            profile.TagNeeds.Add(BuildNeed(target, CountNeedTarget(analysis, target), minimum, maximum));
        }

        if (intent is not null)
        {
            profile.Notes.Add("Deck intent targets override heuristic profile thresholds where present.");
        }

        profile.Notes.Add($"Using {heuristicProfile.Name} targets from {DeckHeuristicProfileCatalog.BuiltInConfigVersion}.");
        profile.Notes.Add("Need rows use reconciled counts: classifier roles/tags first, then all matching user categories when category evidence is stronger.");
        profile.Notes.AddRange(heuristicProfile.Notes);
        return profile;
    }

    /// <summary>
    /// Counts a need target from classifier roles, classifier tags, or explicit workspace categories.
    /// </summary>
    private static NeedTargetCount CountNeedTarget(DeckAnalysis analysis, string target)
    {
        int roleCount = Count(analysis.RoleCounts, target);
        int tagCount = Count(analysis.TagCounts, target);
        int allCategoryCount = Count(analysis.IncludedAllCategoryCounts, target);

        if (DeckRoles.Primary.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            if (allCategoryCount > roleCount)
            {
                return new NeedTargetCount(allCategoryCount, "all user categories");
            }

            if (roleCount > 0 && allCategoryCount > 0)
            {
                return new NeedTargetCount(roleCount, "heuristic roles and user categories agree");
            }

            return new NeedTargetCount(roleCount, "heuristic functional roles");
        }

        if (DeckTags.Secondary.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            if (allCategoryCount > tagCount)
            {
                return new NeedTargetCount(allCategoryCount, "all user categories");
            }

            if (tagCount > 0 && allCategoryCount > 0)
            {
                return new NeedTargetCount(tagCount, "classifier tags and user categories agree");
            }

            return new NeedTargetCount(tagCount, "classifier secondary tags");
        }

        if (tagCount > 0)
        {
            return new NeedTargetCount(tagCount, "classifier secondary tags");
        }

        if (allCategoryCount > 0)
        {
            return new NeedTargetCount(allCategoryCount, "all user categories");
        }

        return new NeedTargetCount(0, "no matching role, tag, or category evidence");
    }

    /// <summary>
    /// Applies deck intent target overrides.
    /// </summary>
    private static Dictionary<string, (int Minimum, int? Maximum)> ApplyIntentTargets(
        IReadOnlyDictionary<string, (int Minimum, int? Maximum)> defaults,
        DeckIntent? intent)
    {
        Dictionary<string, (int Minimum, int? Maximum)> targets = new(defaults, StringComparer.OrdinalIgnoreCase);
        if (intent is null)
        {
            return targets;
        }

        foreach ((string target, DeckIntentTarget intentTarget) in intent.Targets)
        {
            int minimum = intentTarget.Minimum ?? (targets.TryGetValue(target, out (int Minimum, int? Maximum) existing) ? existing.Minimum : 0);
            int? maximum = intentTarget.Maximum ?? (targets.TryGetValue(target, out existing) ? existing.Maximum : null);
            targets[target] = (minimum, maximum);
        }

        return targets;
    }

    /// <summary>
    /// Resolves the requested best-practice profile.
    /// </summary>
    private static BestPracticeProfileResolution ResolveBestPracticeProfile(string? requestedProfile, DeckIntent? intent)
    {
        if (!string.IsNullOrWhiteSpace(requestedProfile)
            && DeckIntentVocabulary.TryNormalizeHeuristicProfile(requestedProfile, out string normalized)
            && normalized != "auto")
        {
            return new BestPracticeProfileResolution(normalized, "tool-parameter");
        }

        if (!string.IsNullOrWhiteSpace(intent?.HeuristicProfile)
            && DeckIntentVocabulary.TryNormalizeHeuristicProfile(intent.HeuristicProfile, out normalized))
        {
            return normalized == "auto"
                ? ResolveExplicitIntentProfile(intent)
                : new BestPracticeProfileResolution(normalized, "deck-intent:heuristic-profile");
        }

        return ResolveExplicitIntentProfile(intent);
    }

    /// <summary>
    /// Resolves a profile from explicit Deck Intent fields.
    /// </summary>
    private static BestPracticeProfileResolution ResolveExplicitIntentProfile(DeckIntent? intent)
    {
        if (DeckIntentVocabulary.TryNormalizePackageTemplate(intent?.PackageTemplate ?? "", out string packageTemplate)
            && packageTemplate is "8x8" or "7x9" or "9x7")
        {
            return new BestPracticeProfileResolution($"package-{packageTemplate}", "deck-intent:package-template");
        }

        string explicitText = $"{intent?.Archetype} {string.Join(' ', intent?.ArchetypeTags ?? [])}".Trim();
        string archetypeProfile = ResolveArchetypeProfile(explicitText);
        if (!string.IsNullOrWhiteSpace(archetypeProfile))
        {
            return new BestPracticeProfileResolution(archetypeProfile, "deck-intent:archetype");
        }

        if (DeckIntentVocabulary.TryNormalizePowerLevel(intent?.PowerLevel ?? "", out string powerLevel)
            && powerLevel == "cedh")
        {
            string text = $"{intent?.Archetype} {string.Join(' ', intent?.ArchetypeTags ?? [])}";
            if (text.Contains("turbo", StringComparison.OrdinalIgnoreCase))
            {
                return new BestPracticeProfileResolution("cedh-turbo", "deck-intent:power-level+archetype");
            }

            if (text.Contains("stax", StringComparison.OrdinalIgnoreCase))
            {
                return new BestPracticeProfileResolution("cedh-stax", "deck-intent:power-level+archetype");
            }

            if (text.Contains("tempo", StringComparison.OrdinalIgnoreCase))
            {
                return new BestPracticeProfileResolution("cedh-tempo", "deck-intent:power-level+archetype");
            }

            return new BestPracticeProfileResolution("cedh-midrange", "deck-intent:power-level");
        }

        return new BestPracticeProfileResolution("commander-baseline", "baseline-default");
    }

    /// <summary>
    /// Maps explicit intent archetype text to built-in profile ids.
    /// </summary>
    private static string ResolveArchetypeProfile(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        string normalized = DeckIntentVocabulary.NormalizeToken(text);
        if (normalized.Contains("landfall", StringComparison.OrdinalIgnoreCase))
        {
            return "archetype-landfall";
        }

        if (normalized.Contains("sea-monster", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("sea-monsters", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("kenessos", StringComparison.OrdinalIgnoreCase))
        {
            return "archetype-sea-monsters";
        }

        if (normalized.Contains("enchantment", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("enchantress", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("yuna", StringComparison.OrdinalIgnoreCase))
        {
            return "archetype-enchantments";
        }

        if (normalized.Contains("go-wide", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("tokens", StringComparison.OrdinalIgnoreCase))
        {
            return "archetype-go-wide";
        }

        return "";
    }

    /// <summary>
    /// Compares a deck to a heuristic profile.
    /// </summary>
    private static DeckHeuristicProfileComparison CompareHeuristicProfile(
        DeckAnalysis deck,
        DeckIntent? intent,
        DeckHeuristicProfile profile)
    {
        DeckHeuristicProfileComparison comparison = new()
        {
            ProfileId = profile.Id,
            Name = profile.Name,
            Notes = profile.Notes.ToList()
        };
        double penalty = 0;
        foreach ((string target, (int minimum, int? maximum)) in profile.RoleTargets)
        {
            penalty += CompareNeed(comparison, target, CountNeedTarget(deck, target).Count, minimum, maximum);
        }

        foreach ((string target, (int minimum, int? maximum)) in profile.TagTargets)
        {
            penalty += CompareNeed(comparison, target, CountNeedTarget(deck, target).Count, minimum, maximum);
        }

        if (profile.Id.Equals("fifty-mana-sources", StringComparison.OrdinalIgnoreCase))
        {
            penalty += CompareNeed(
                comparison,
                "Mana Sources",
                Count(deck.RoleCounts, DeckRoles.Lands) + Count(deck.RoleCounts, DeckRoles.Ramp),
                50,
                null);
        }

        if (profile.Id.StartsWith("package-", StringComparison.OrdinalIgnoreCase))
        {
            int expectedPackages = profile.Id switch
            {
                "package-7x9" => 7,
                "package-9x7" => 9,
                _ => 8
            };
            if (intent?.Packages.Count > 0)
            {
                comparison.Notes.Add($"Deck intent defines {intent.Packages.Count} packages; {profile.Name} expects about {expectedPackages}.");
            }
            else
            {
                comparison.Notes.Add($"{profile.Name} works best when Deck Intent includes a Packages section.");
                penalty += 2;
            }
        }

        comparison.FitScore = Math.Round(Math.Clamp(100 - (penalty * 6), 0, 100), 1);
        comparison.Status = comparison.FitScore >= 85
            ? "strong-fit"
            : comparison.FitScore >= 65 ? "partial-fit" : "poor-fit";
        return comparison;
    }

    /// <summary>
    /// Adds gap or overage information for a target.
    /// </summary>
    private static double CompareNeed(
        DeckHeuristicProfileComparison comparison,
        string target,
        int current,
        int minimum,
        int? maximum)
    {
        if (current < minimum)
        {
            int gap = minimum - current;
            comparison.Gaps.Add($"{target}: {current}/{minimum}+");
            return Math.Min(3, gap);
        }

        if (maximum.HasValue && current > maximum.Value)
        {
            int overage = current - maximum.Value;
            comparison.Overages.Add($"{target}: {current}/{maximum.Value} max");
            return Math.Min(3, overage);
        }

        return 0;
    }

    /// <summary>
    /// Builds a single need row.
    /// </summary>
    private static DeckNeed BuildNeed(string target, NeedTargetCount current, int minimum, int? maximum)
    {
        string countSource = current.Source;
        string countSourceNote = $" Count source: {countSource}.";
        string status = current.Count < minimum ? "low" : maximum.HasValue && current.Count > maximum.Value ? "high" : "ok";
        string rationale = status switch
        {
            "low" => $"{target} is low at {current.Count}; target at least {minimum}.{countSourceNote}",
            "high" => $"{target} is high at {current.Count}; target no more than {maximum}.{countSourceNote}",
            _ => $"{target} is in range at {current.Count}.{countSourceNote}"
        };
        return new DeckNeed
        {
            Target = target,
            CurrentCount = current.Count,
            CountSource = countSource,
            Minimum = minimum,
            Maximum = maximum,
            Status = status,
            Rationale = rationale
        };
    }

    /// <summary>
    /// Builds citations for default heuristics.
    /// </summary>
    private static List<DeckCitation> BuildBestPracticeCitations()
    {
        return
        [
            new DeckCitation
            {
                Key = "commander-heuristics",
                Title = "Common Commander deck construction heuristics",
                Uri = "https://edhrec.com/guides/how-to-build-a-commander-deck",
                Notes = "Default role targets reflect common Commander deckbuilding guidance for lands, ramp, draw, interaction, wipes, protection, and finishers."
            },
            new DeckCitation
            {
                Key = "command-zone-template",
                Title = "Command Zone Template",
                Uri = "https://edh.fandom.com/wiki/Command_Zone_Template",
                Notes = "Provides common role-count templates for ramp, draw, removal, and board wipes."
            },
            new DeckCitation
            {
                Key = "package-theory",
                Title = "8x8, 7x9, and 9x7 package theory",
                Uri = "https://edh.fandom.com/wiki/7_by_9",
                Notes = "Package profiles model decks as functional card groups rather than one universal role checklist."
            },
            new DeckCitation
            {
                Key = "commander-brackets",
                Title = "Commander Brackets beta context",
                Uri = "https://magic.wizards.com/en/news/announcements/commander-brackets-beta-update-february-9-2026",
                Notes = "Bracket and power outputs are advisory for pregame discussion rather than official determinations."
            }
        ];
    }

    /// <summary>
    /// Carries the selected best-practice profile and its explicit source.
    /// </summary>
    private sealed record BestPracticeProfileResolution(string ProfileId, string Source);

    /// <summary>
    /// Carries a reconciled best-practice count and the source that made it authoritative.
    /// </summary>
    private sealed record NeedTargetCount(int Count, string Source);

}
