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
        List<CommanderHeuristicProfile> profiles = BuildCommanderHeuristicProfiles();
        string selectedProfileId = ResolveBestPracticeProfile(profile, intent);
        if (selectedProfileId == "auto")
        {
            selectedProfileId = InferBestPracticeProfile(intent);
        }

        CommanderHeuristicProfile selectedProfile = profiles.FirstOrDefault(value => value.Id.Equals(selectedProfileId, StringComparison.OrdinalIgnoreCase))
            ?? profiles[0];
        DeckNeedProfile needProfile = BuildNeedProfile(workspace, intent, selectedProfile);
        ManaBaseAnalysis mana = AnalyzeManaBase(workspace);
        DeckConsistencyAnalysis consistency = AnalyzeDeckConsistency(workspace);
        DeckAnalysis deck = DeckAnalyzer.Analyze(workspace);
        DeckBestPracticeAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            NeedProfile = needProfile,
            RecommendedProfile = selectedProfile.Id,
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
        return analysis;
    }

    /// <summary>
    /// Builds need rows from role and tag counts.
    /// </summary>
    private static DeckNeedProfile BuildNeedProfile(
        DeckWorkspace workspace,
        DeckIntent? intent,
        CommanderHeuristicProfile heuristicProfile)
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
            profile.RoleNeeds.Add(BuildNeed(target, Count(analysis.RoleCounts, target), minimum, maximum));
        }

        foreach ((string target, (int minimum, int? maximum)) in tagTargets)
        {
            profile.TagNeeds.Add(BuildNeed(target, Count(analysis.TagCounts, target), minimum, maximum));
        }

        if (intent is not null)
        {
            profile.Notes.Add("Deck intent targets override heuristic profile thresholds where present.");
        }

        profile.Notes.Add($"Using {heuristicProfile.Name} targets.");
        profile.Notes.AddRange(heuristicProfile.Notes);
        return profile;
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
    private static string ResolveBestPracticeProfile(string? requestedProfile, DeckIntent? intent)
    {
        if (!string.IsNullOrWhiteSpace(requestedProfile)
            && DeckIntentVocabulary.TryNormalizeHeuristicProfile(requestedProfile, out string normalized)
            && normalized != "auto")
        {
            return normalized;
        }

        if (!string.IsNullOrWhiteSpace(intent?.HeuristicProfile)
            && DeckIntentVocabulary.TryNormalizeHeuristicProfile(intent.HeuristicProfile, out normalized))
        {
            return normalized;
        }

        return "auto";
    }

    /// <summary>
    /// Infers a best-practice profile from deck intent.
    /// </summary>
    private static string InferBestPracticeProfile(DeckIntent? intent)
    {
        if (DeckIntentVocabulary.TryNormalizePackageTemplate(intent?.PackageTemplate ?? "", out string packageTemplate)
            && packageTemplate is "8x8" or "7x9" or "9x7")
        {
            return $"package-{packageTemplate}";
        }

        if (DeckIntentVocabulary.TryNormalizePowerLevel(intent?.PowerLevel ?? "", out string powerLevel)
            && powerLevel == "cedh")
        {
            string text = $"{intent?.Archetype} {string.Join(' ', intent?.Prefer ?? [])}";
            if (text.Contains("turbo", StringComparison.OrdinalIgnoreCase))
            {
                return "cedh-turbo";
            }

            if (text.Contains("stax", StringComparison.OrdinalIgnoreCase))
            {
                return "cedh-stax";
            }

            if (text.Contains("tempo", StringComparison.OrdinalIgnoreCase))
            {
                return "cedh-tempo";
            }

            return "cedh-midrange";
        }

        return "commander-baseline";
    }

    /// <summary>
    /// Compares a deck to a heuristic profile.
    /// </summary>
    private static DeckHeuristicProfileComparison CompareHeuristicProfile(
        DeckAnalysis deck,
        DeckIntent? intent,
        CommanderHeuristicProfile profile)
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
            penalty += CompareNeed(comparison, target, Count(deck.RoleCounts, target), minimum, maximum);
        }

        foreach ((string target, (int minimum, int? maximum)) in profile.TagTargets)
        {
            penalty += CompareNeed(comparison, target, Count(deck.TagCounts, target), minimum, maximum);
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
    /// Builds built-in Commander heuristic profiles.
    /// </summary>
    private static List<CommanderHeuristicProfile> BuildCommanderHeuristicProfiles()
    {
        return
        [
            Profile(
                "commander-baseline",
                "Commander baseline",
                RoleTargets((DeckRoles.Lands, 35, 39), (DeckRoles.Ramp, 8, 12), (DeckRoles.Draw, 8, 12), (DeckRoles.Interaction, 8, 13), (DeckRoles.BoardWipes, 2, 5), (DeckRoles.Protection, 2, 6), (DeckRoles.Recursion, 2, 6), (DeckRoles.Wincons, 2, 6)),
                TagTargets((DeckTags.GraveyardHate, 1, 4), (DeckTags.ArtifactEnchantmentHate, 2, 6), (DeckTags.TableInteraction, 2, null), (DeckTags.TokenHate, 1, 4), (DeckTags.Finishers, 2, 6)),
                "Broad default Commander thresholds."),
            Profile(
                "command-zone-template",
                "Command Zone template",
                RoleTargets((DeckRoles.Lands, 35, 38), (DeckRoles.Ramp, 10, 12), (DeckRoles.Draw, 10, 12), (DeckRoles.Interaction, 10, 12), (DeckRoles.BoardWipes, 3, 4), (DeckRoles.Wincons, 2, 5)),
                TagTargets((DeckTags.GraveyardHate, 1, 3), (DeckTags.ArtifactEnchantmentHate, 2, 5)),
                "Classic ramp, draw, removal, and wipe package template."),
            Profile(
                "edhrec-foundation",
                "EDHREC foundation",
                RoleTargets((DeckRoles.Lands, 36, 39), (DeckRoles.Ramp, 10, 12), (DeckRoles.Draw, 10, 14), (DeckRoles.Interaction, 8, 12), (DeckRoles.BoardWipes, 2, 5), (DeckRoles.Recursion, 2, 5), (DeckRoles.Wincons, 2, 5)),
                TagTargets((DeckTags.GraveyardHate, 1, 3), (DeckTags.ArtifactEnchantmentHate, 2, 5), (DeckTags.Finishers, 2, 5)),
                "EDHREC-style foundation for mana, velocity, interaction, and game enders."),
            Profile(
                "mana-rich-39-land",
                "Mana-rich 39-land baseline",
                RoleTargets((DeckRoles.Lands, 39, 39), (DeckRoles.Ramp, 8, 12), (DeckRoles.Draw, 8, 12), (DeckRoles.Interaction, 8, 13)),
                TagTargets(),
                "Useful for higher curves, landfall, and decks that need stable early land drops."),
            Profile(
                "fifty-mana-sources",
                "Fifty mana sources",
                RoleTargets((DeckRoles.Lands, 36, 40), (DeckRoles.Ramp, 10, 14), (DeckRoles.Draw, 8, 12), (DeckRoles.Interaction, 8, 13)),
                TagTargets(),
                "Checks lands plus ramp against the 50-source mana heuristic."),
            Profile(
                "package-8x8",
                "8x8 package template",
                RoleTargets((DeckRoles.Lands, 35, 36), (DeckRoles.Ramp, 8, 10), (DeckRoles.Draw, 8, 10), (DeckRoles.Interaction, 8, 10)),
                TagTargets(),
                "Commander plus lands, then eight functional packages of about eight cards."),
            Profile(
                "package-7x9",
                "7x9 package template",
                RoleTargets((DeckRoles.Lands, 36, 37), (DeckRoles.Ramp, 7, 10), (DeckRoles.Draw, 7, 10), (DeckRoles.Interaction, 7, 10)),
                TagTargets(),
                "Commander plus lands, then seven larger packages."),
            Profile(
                "package-9x7",
                "9x7 package template",
                RoleTargets((DeckRoles.Lands, 35, 36), (DeckRoles.Ramp, 7, 9), (DeckRoles.Draw, 7, 9), (DeckRoles.Interaction, 7, 9)),
                TagTargets(),
                "Commander plus lands, then nine tighter packages."),
            Profile(
                "seventy-five-percent",
                "75 percent Commander",
                RoleTargets((DeckRoles.Lands, 36, 38), (DeckRoles.Ramp, 8, 10), (DeckRoles.Draw, 9, 12), (DeckRoles.Interaction, 10, 13), (DeckRoles.Tutors, 0, 2), (DeckRoles.BoardWipes, 2, 4), (DeckRoles.Wincons, 2, 4)),
                TagTargets((DeckTags.Finishers, 2, 4)),
                "Strong, interactive, and scalable without maximizing deterministic consistency."),
            Profile(
                "cedh-turbo",
                "cEDH turbo",
                RoleTargets((DeckRoles.Lands, 27, 31), (DeckRoles.Ramp, 14, 20), (DeckRoles.Tutors, 8, 14), (DeckRoles.Interaction, 10, 16), (DeckRoles.BoardWipes, 0, 1)),
                TagTargets((DeckTags.CardSelection, 8, 14), (DeckTags.ComboPiece, 5, 10)),
                "Fast mana, compact wins, tutors, and cheap interaction."),
            Profile(
                "cedh-midrange",
                "cEDH midrange",
                RoleTargets((DeckRoles.Lands, 28, 32), (DeckRoles.Ramp, 10, 16), (DeckRoles.Tutors, 6, 12), (DeckRoles.Interaction, 14, 20)),
                TagTargets((DeckTags.CardSelection, 8, 14), (DeckTags.ComboPiece, 3, 8)),
                "Compact wins with more interaction and value than turbo shells."),
            Profile(
                "cedh-stax",
                "cEDH stax",
                RoleTargets((DeckRoles.Lands, 29, 33), (DeckRoles.Ramp, 9, 14), (DeckRoles.Tutors, 5, 10), (DeckRoles.Interaction, 12, 18)),
                TagTargets((DeckTags.Stax, 6, 12), (DeckTags.ComboPiece, 2, 7)),
                "Permission, taxes, hate pieces, and compact win routes."),
            Profile(
                "cedh-tempo",
                "cEDH tempo",
                RoleTargets((DeckRoles.Lands, 28, 32), (DeckRoles.Ramp, 9, 14), (DeckRoles.Tutors, 5, 10), (DeckRoles.Interaction, 14, 20)),
                TagTargets((DeckTags.CardSelection, 8, 14), (DeckTags.ComboPiece, 2, 7)),
                "Low curve, high interaction, and efficient pressure.")
        ];
    }

    /// <summary>
    /// Creates a heuristic profile.
    /// </summary>
    private static CommanderHeuristicProfile Profile(
        string id,
        string name,
        Dictionary<string, (int Minimum, int? Maximum)> roleTargets,
        Dictionary<string, (int Minimum, int? Maximum)> tagTargets,
        params string[] notes)
    {
        return new CommanderHeuristicProfile(id, name, roleTargets, tagTargets, notes.ToList());
    }

    /// <summary>
    /// Creates role targets.
    /// </summary>
    private static Dictionary<string, (int Minimum, int? Maximum)> RoleTargets(params (string Target, int Minimum, int? Maximum)[] targets)
    {
        return Targets(targets);
    }

    /// <summary>
    /// Creates tag targets.
    /// </summary>
    private static Dictionary<string, (int Minimum, int? Maximum)> TagTargets(params (string Target, int Minimum, int? Maximum)[] targets)
    {
        return Targets(targets);
    }

    /// <summary>
    /// Creates generic targets.
    /// </summary>
    private static Dictionary<string, (int Minimum, int? Maximum)> Targets(params (string Target, int Minimum, int? Maximum)[] targets)
    {
        Dictionary<string, (int Minimum, int? Maximum)> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string target, int minimum, int? maximum) in targets)
        {
            result[target] = (minimum, maximum);
        }

        return result;
    }

    /// <summary>
    /// Builds a single need row.
    /// </summary>
    private static DeckNeed BuildNeed(string target, int current, int minimum, int? maximum)
    {
        string status = current < minimum ? "low" : maximum.HasValue && current > maximum.Value ? "high" : "ok";
        string rationale = status switch
        {
            "low" => $"{target} is low at {current}; target at least {minimum}.",
            "high" => $"{target} is high at {current}; target no more than {maximum}.",
            _ => $"{target} is in range at {current}."
        };
        return new DeckNeed
        {
            Target = target,
            CurrentCount = current,
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
    /// Stores a built-in Commander heuristic profile.
    /// </summary>
    private sealed class CommanderHeuristicProfile
    {
        /// <summary>
        /// Creates a Commander heuristic profile.
        /// </summary>
        public CommanderHeuristicProfile(
            string id,
            string name,
            IReadOnlyDictionary<string, (int Minimum, int? Maximum)> roleTargets,
            IReadOnlyDictionary<string, (int Minimum, int? Maximum)> tagTargets,
            IReadOnlyList<string> notes)
        {
            Id = id;
            Name = name;
            RoleTargets = roleTargets;
            TagTargets = tagTargets;
            Notes = notes;
        }

        /// <summary>
        /// Gets the profile id.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the profile name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets role target bands.
        /// </summary>
        public IReadOnlyDictionary<string, (int Minimum, int? Maximum)> RoleTargets { get; }

        /// <summary>
        /// Gets tag target bands.
        /// </summary>
        public IReadOnlyDictionary<string, (int Minimum, int? Maximum)> TagTargets { get; }

        /// <summary>
        /// Gets profile notes.
        /// </summary>
        public IReadOnlyList<string> Notes { get; }
    }

}

