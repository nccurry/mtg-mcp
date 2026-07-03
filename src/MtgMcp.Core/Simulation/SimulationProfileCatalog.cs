using System.Globalization;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Stores built-in and optional external simulation profiles.
/// </summary>
public sealed class SimulationProfileCatalog
{
    /// <summary>
    /// Keeps profile JSON reading deterministic and compatible with app configuration.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Stores profiles keyed by normalized id.
    /// </summary>
    private readonly Dictionary<string, SimulationProfile> profiles;

    /// <summary>
    /// Gets warnings produced while loading configured profile files.
    /// </summary>
    public IReadOnlyList<string> ConfigurationWarnings { get; }

    /// <summary>
    /// Creates a catalog from built-ins and optional external profiles.
    /// </summary>
    public SimulationProfileCatalog(
        IEnumerable<SimulationProfile>? externalProfiles = null,
        bool allowExternalProfileOverrides = true,
        IEnumerable<string>? configurationWarnings = null)
    {
        profiles = BuiltInProfiles()
            .ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

        List<SimulationProfile> external = (externalProfiles ?? []).ToList();
        List<string> warnings = [];
        warnings.AddRange(configurationWarnings ?? []);
        warnings.AddRange(ValidateProfiles(external));
        foreach (SimulationProfile profile in MergeExternalProfiles(external, profiles, warnings))
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                continue;
            }

            string normalizedId = NormalizeProfileId(profile.Id);
            profile.Id = normalizedId;
            if (!allowExternalProfileOverrides && profiles.ContainsKey(normalizedId))
            {
                continue;
            }

            SimulationProfile safeProfile = Clone(profile);
            safeProfile.WinRoutes = safeProfile.WinRoutes
                .Where(route => route.Requirements.All(SimulationRouteEvaluator.IsSupportedRequirement))
                .ToList();
            profiles[normalizedId] = safeProfile;
        }

        ConfigurationWarnings = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets all known profiles.
    /// </summary>
    public IReadOnlyList<SimulationProfile> Profiles => profiles.Values
        .OrderBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
        .Select(Clone)
        .ToList();

    /// <summary>
    /// Creates the default catalog.
    /// </summary>
    public static SimulationProfileCatalog CreateDefault()
    {
        return new SimulationProfileCatalog();
    }

    /// <summary>
    /// Creates a neutral profile instance.
    /// </summary>
    public static SimulationProfile NeutralProfile()
    {
        return BuiltInProfiles().First(profile => profile.Id == SimulationProfileIds.Neutral);
    }

    /// <summary>
    /// Deserializes profile files and validates their basic shape.
    /// </summary>
    public static (List<SimulationProfile> Profiles, List<string> Warnings) ReadProfileFiles(IEnumerable<string> paths)
    {
        List<SimulationProfile> loaded = [];
        List<string> warnings = [];
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!File.Exists(path))
            {
                warnings.Add($"Simulation profile file '{path}' was not found.");
                continue;
            }

            ReadProfileFile(path, loaded, warnings);
        }

        warnings.AddRange(ValidateProfiles(loaded));
        return (loaded, warnings);
    }

    /// <summary>
    /// Reads one existing profile file and converts expected I/O or JSON failures into warnings.
    /// </summary>
    private static void ReadProfileFile(
        string path,
        List<SimulationProfile> loaded,
        List<string> warnings)
    {
        try
        {
            string json = File.ReadAllText(path);
            AddProfilesFromJson(path, json, loaded, warnings);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            warnings.Add($"Simulation profile file '{path}' could not be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Adds a single profile or profile array from one JSON document.
    /// </summary>
    private static void AddProfilesFromJson(
        string path,
        string json,
        List<SimulationProfile> loaded,
        List<string> warnings)
    {
        if (json.TrimStart().StartsWith('['))
        {
            List<SimulationProfile>? many = JsonSerializer.Deserialize<List<SimulationProfile>>(json, JsonOptions);
            if (many is not null)
            {
                loaded.AddRange(many.Where(profile => !string.IsNullOrWhiteSpace(profile.Id)));
            }

            return;
        }

        SimulationProfile? single = JsonSerializer.Deserialize<SimulationProfile>(json, JsonOptions);
        if (single is not null && !string.IsNullOrWhiteSpace(single.Id))
        {
            loaded.Add(single);
            return;
        }

        warnings.Add($"Simulation profile file '{path}' did not contain a profile or profile array.");
    }

    /// <summary>
    /// Resolves the active profile for a deck.
    /// </summary>
    public ResolvedSimulationProfile Resolve(
        DeckWorkspace workspace,
        string? requestedProfile,
        DeckIntent? intent)
    {
        ResolvedSimulationProfile result = new();
        result.Warnings.AddRange(ConfigurationWarnings);
        List<SimulationProfileEvidence> candidates = BuildAutoCandidates(workspace, intent);
        result.Candidates = candidates;

        string? explicitProfile = NormalizeRequestedProfile(requestedProfile);
        if (!string.IsNullOrWhiteSpace(explicitProfile)
            && !explicitProfile.Equals(SimulationProfileIds.Auto, StringComparison.OrdinalIgnoreCase)
            && !explicitProfile.Equals("commander-default", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGet(explicitProfile, out SimulationProfile profile))
            {
                result.Profile = ApplyIntentOverrides(profile, intent, result);
                result.Source = "explicit";
                result.Evidence.Add(new SimulationProfileEvidence
                {
                    Source = "tool-argument",
                    ProfileId = result.Profile.Id,
                    Score = 1,
                    Message = $"Explicit simulation profile '{explicitProfile}' was requested."
                });
                return result;
            }

            result.Warnings.Add($"Simulation profile '{requestedProfile}' was not found; falling back to deck intent, auto inference, or neutral.");
        }

        string? intentProfile = NormalizeRequestedProfile(intent?.SimulationProfile);
        if (!string.IsNullOrWhiteSpace(intentProfile)
            && !intentProfile.Equals(SimulationProfileIds.Auto, StringComparison.OrdinalIgnoreCase))
        {
            if (TryGet(intentProfile, out SimulationProfile profile))
            {
                result.Profile = ApplyIntentOverrides(profile, intent, result);
                result.Source = "deck-intent";
                result.Evidence.Add(new SimulationProfileEvidence
                {
                    Source = "deck-intent",
                    ProfileId = result.Profile.Id,
                    Score = 1,
                    Message = $"Deck intent selected simulation profile '{intentProfile}'."
                });
                return result;
            }

            result.Warnings.Add($"Deck intent simulation profile '{intent?.SimulationProfile}' was not found; falling back to auto inference or neutral.");
        }

        SimulationProfileEvidence? best = candidates
            .Where(candidate => !candidate.ProfileId.Equals(SimulationProfileIds.Neutral, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.Score >= 3)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ProfileId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (best is not null && TryGet(best.ProfileId, out SimulationProfile inferred))
        {
            result.Profile = ApplyIntentOverrides(inferred, intent, result);
            result.Source = "auto";
            result.Evidence.Add(new SimulationProfileEvidence
            {
                Source = "auto-profile",
                ProfileId = result.Profile.Id,
                Score = best.Score,
                Message = best.Message
            });
            return result;
        }

        result.Profile = ApplyIntentOverrides(GetOrNeutral(SimulationProfileIds.Neutral), intent, result);
        result.Source = "default";
        result.Evidence.Add(new SimulationProfileEvidence
        {
            Source = "default",
            ProfileId = result.Profile.Id,
            Message = "No stronger profile signal was found; using neutral least-assumption simulation."
        });
        return result;
    }

    /// <summary>
    /// Gets a known profile by id.
    /// </summary>
    public bool TryGet(string id, out SimulationProfile profile)
    {
        if (profiles.TryGetValue(NormalizeProfileId(id), out SimulationProfile? stored))
        {
            profile = Clone(stored);
            return true;
        }

        profile = null!;
        return false;
    }

    /// <summary>
    /// Validates external profile ids, inheritance references, and route predicates.
    /// </summary>
    private static List<string> ValidateProfiles(IEnumerable<SimulationProfile> values)
    {
        List<string> warnings = [];
        List<SimulationProfile> profilesToValidate = values.ToList();
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> known = BuiltInProfiles()
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (SimulationProfile profile in profilesToValidate)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                warnings.Add("An external simulation profile was ignored because it had no id.");
                continue;
            }

            string id = NormalizeProfileId(profile.Id);
            if (!ids.Add(id))
            {
                warnings.Add($"External simulation profile '{id}' was defined more than once.");
            }

            foreach (string parent in profile.Inherits)
            {
                string normalizedParent = NormalizeProfileId(parent);
                if (!known.Contains(normalizedParent)
                    && !profilesToValidate.Any(value => NormalizeProfileId(value.Id).Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add($"External simulation profile '{id}' inherits unknown profile '{parent}'.");
                }
            }

            ValidateRouteRequirements(profile, id, warnings);
        }

        return warnings;
    }

    /// <summary>
    /// Adds warnings for unsupported requirements in one profile's configured routes.
    /// </summary>
    private static void ValidateRouteRequirements(
        SimulationProfile profile,
        string profileId,
        List<string> warnings)
    {
        foreach (SimulationRouteDefinition route in profile.WinRoutes)
        {
            foreach (string requirement in route.Requirements)
            {
                if (!SimulationRouteEvaluator.IsSupportedRequirement(requirement))
                {
                    warnings.Add(
                        $"Simulation profile '{profileId}' route '{route.Name}' "
                            + $"has unsupported requirement '{requirement}'.");
                }
            }
        }
    }

    /// <summary>
    /// Applies external profile inheritance before adding profiles to the catalog.
    /// </summary>
    private static List<SimulationProfile> MergeExternalProfiles(
        IReadOnlyList<SimulationProfile> external,
        IReadOnlyDictionary<string, SimulationProfile> builtIns,
        List<string> warnings)
    {
        Dictionary<string, SimulationProfile> byId = external
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
            .GroupBy(profile => NormalizeProfileId(profile.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SimulationProfile> resolved = new(StringComparer.OrdinalIgnoreCase);

        foreach (SimulationProfile profile in external)
        {
            if (!string.IsNullOrWhiteSpace(profile.Id)
                && TryResolveExternalProfile(
                    NormalizeProfileId(profile.Id),
                    byId,
                    builtIns,
                    resolved,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    warnings,
                    out SimulationProfile merged))
            {
                resolved[merged.Id] = merged;
            }
        }

        return resolved.Values.ToList();
    }

    /// <summary>
    /// Resolves one external profile and its parents without executing arbitrary configuration.
    /// </summary>
    private static bool TryResolveExternalProfile(
        string profileId,
        IReadOnlyDictionary<string, SimulationProfile> external,
        IReadOnlyDictionary<string, SimulationProfile> builtIns,
        Dictionary<string, SimulationProfile> resolved,
        HashSet<string> resolving,
        List<string> warnings,
        out SimulationProfile merged)
    {
        if (resolved.TryGetValue(profileId, out SimulationProfile? cached))
        {
            merged = Clone(cached);
            return true;
        }

        if (!external.TryGetValue(profileId, out SimulationProfile? child))
        {
            merged = null!;
            return false;
        }

        if (!resolving.Add(profileId))
        {
            warnings.Add($"External simulation profile '{profileId}' has a cyclic inheritance chain.");
            merged = null!;
            return false;
        }

        merged = Clone(child);
        foreach (string parentId in child.Inherits.Select(NormalizeProfileId))
        {
            SimulationProfile? parent = null;
            if (builtIns.TryGetValue(parentId, out SimulationProfile? builtInParent))
            {
                parent = builtInParent;
            }
            else if (TryResolveExternalProfile(parentId, external, builtIns, resolved, resolving, warnings, out SimulationProfile externalParent))
            {
                parent = externalParent;
            }

            if (parent is null)
            {
                continue;
            }

            merged = MergeProfile(parent, merged);
        }

        resolving.Remove(profileId);
        resolved[profileId] = Clone(merged);
        return true;
    }

    /// <summary>
    /// Merges a child profile over a parent profile using defaults as the unset value.
    /// </summary>
    private static SimulationProfile MergeProfile(SimulationProfile parent, SimulationProfile child)
    {
        SimulationProfile merged = Clone(parent);
        merged.Id = NormalizeProfileId(child.Id);
        merged.Name = string.IsNullOrWhiteSpace(child.Name) ? parent.Name : child.Name;
        merged.Description = string.IsNullOrWhiteSpace(child.Description) ? parent.Description : child.Description;
        merged.Inherits = child.Inherits.ToList();
        merged.ThemeTags = parent.ThemeTags
            .Concat(child.ThemeTags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        merged.Mulligan = IsDefaultSettings(child.Mulligan) ? Clone(parent.Mulligan) : Clone(child.Mulligan);
        merged.Sequencing = IsDefaultSettings(child.Sequencing) ? Clone(parent.Sequencing) : Clone(child.Sequencing);
        merged.Scenarios = IsDefaultSettings(child.Scenarios) ? Clone(parent.Scenarios) : Clone(child.Scenarios);
        merged.WinDetection = IsDefaultSettings(child.WinDetection) ? Clone(parent.WinDetection) : Clone(child.WinDetection);
        merged.WinRoutes = parent.WinRoutes
            .Concat(child.WinRoutes)
            .Select(Clone)
            .ToList();
        return merged;
    }

    /// <summary>
    /// Gets a profile by id or returns the neutral built-in.
    /// </summary>
    private SimulationProfile GetOrNeutral(string id)
    {
        return TryGet(id, out SimulationProfile profile)
            ? profile
            : NeutralProfile();
    }

    /// <summary>
    /// Applies deck-local simulation settings and win routes to a cloned profile.
    /// </summary>
    private SimulationProfile ApplyIntentOverrides(
        SimulationProfile profile,
        DeckIntent? intent,
        ResolvedSimulationProfile result)
    {
        SimulationProfile resolved = Clone(profile);
        if (intent is null)
        {
            return resolved;
        }

        foreach (string tag in intent.ArchetypeTags)
        {
            AddUnique(resolved.ThemeTags, tag);
        }

        if (intent.TargetGoldfishTurn.HasValue)
        {
            resolved.WinDetection.FallbackComboEarliestTurn = Math.Min(
                resolved.WinDetection.FallbackComboEarliestTurn,
                intent.TargetGoldfishTurn.Value);
            resolved.WinDetection.FinisherEarliestTurn = Math.Min(
                resolved.WinDetection.FinisherEarliestTurn,
                intent.TargetGoldfishTurn.Value);
            result.Evidence.Add(new SimulationProfileEvidence
            {
                Source = "deck-intent",
                ProfileId = resolved.Id,
                Score = intent.TargetGoldfishTurn.Value,
                Message = $"Target goldfish turn {intent.TargetGoldfishTurn.Value} adjusted route timing assumptions."
            });
        }

        DeckIntentSimulationSettings settings = intent.Simulation;
        if (settings.HoldInteractionFromTurn.HasValue)
        {
            resolved.Sequencing.HoldInteractionFromTurn = Math.Max(1, settings.HoldInteractionFromTurn.Value);
        }

        if (settings.MinimumInteractionHeld.HasValue)
        {
            resolved.Sequencing.MinimumInteractionHeld = Math.Max(0, settings.MinimumInteractionHeld.Value);
        }

        if (settings.PreferCommanderOnCurve.HasValue)
        {
            resolved.Sequencing.PreferCommanderOnCurve = settings.PreferCommanderOnCurve.Value;
        }

        if (settings.PreferredCommanderTurn.HasValue)
        {
            int turn = Math.Max(1, settings.PreferredCommanderTurn.Value);
            resolved.Sequencing.PreferredCommanderTurn = turn;
            resolved.Scenarios.CommanderTurn = turn;
        }

        if (settings.PreferredBackgroundTurn.HasValue)
        {
            resolved.Sequencing.PreferredBackgroundTurn = Math.Max(1, settings.PreferredBackgroundTurn.Value);
        }

        if (settings.CommandZoneOrder.Count > 0)
        {
            resolved.Sequencing.CommandZoneOrder = settings.CommandZoneOrder
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (settings.AcceptShieldDownWinAttempt.HasValue)
        {
            resolved.Sequencing.HoldProtectionWhenCommanderOnline = !settings.AcceptShieldDownWinAttempt.Value;
        }

        foreach (DeckIntentWinRoute route in intent.WinRoutes)
        {
            resolved.WinRoutes.Add(new SimulationRouteDefinition
            {
                Name = route.Name,
                Kind = route.Kind,
                EarliestTurn = Math.Max(1, route.EarliestTurn ?? 1),
                Requirements = route.Requirements.ToList(),
                Source = "deck-intent"
            });
        }

        if (intent.Simulation.Values.Count > 0 || intent.WinRoutes.Count > 0 || intent.ArchetypeTags.Count > 0)
        {
            result.Evidence.Add(new SimulationProfileEvidence
            {
                Source = "deck-intent",
                ProfileId = resolved.Id,
                Message = "Deck intent simulation settings, archetype tags, or win routes were applied."
            });
        }

        return resolved;
    }

    /// <summary>
    /// Scores broad built-in profiles from deck facts and plain-language intent.
    /// </summary>
    private List<SimulationProfileEvidence> BuildAutoCandidates(DeckWorkspace workspace, DeckIntent? intent)
    {
        List<DeckCard> included = IncludedCards(workspace).ToList();
        string intentText = string.Join(
            ' ',
            intent?.Goal,
            intent?.Archetype,
            string.Join(' ', intent?.ArchetypeTags ?? []),
            string.Join(' ', intent?.Prefer ?? []));
        int creatureCount = included.Count(card => ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature"));
        int ramp = CountRole(included, DeckRoles.Ramp);
        int draw = CountRole(included, DeckRoles.Draw);
        int interaction = CountRole(included, DeckRoles.Interaction) + CountRole(included, DeckRoles.BoardWipes);
        int tutors = CountRole(included, DeckRoles.Tutors);
        int comboTags = CountTag(included, DeckTags.ComboPiece) + CountTag(included, DeckTags.ComboEnabler);
        int engines = CountTag(included, DeckTags.Engines);
        int stax = CountTag(included, DeckTags.Stax);
        int tokens = CountTag(included, DeckTags.Tokens) + CountTag(included, DeckTags.SacrificeFodder);
        int voltron = CountTag(included, DeckTags.Voltron);
        int blink = CountTag(included, DeckTags.Blink);
        int reanimator = CountTag(included, DeckTags.Reanimation);
        int averageManaValueCards = included.Count(card => (GetSnapshot(card).ManaValue ?? 0) >= 6);

        List<SimulationProfileEvidence> candidates =
        [
            Candidate(SimulationProfileIds.Combo,
                Score(
                    (ContainsAny(intentText, "combo", "storm", "loop", "turbo") ? 4 : 0),
                    Math.Min(4, comboTags),
                    Math.Min(3, tutors),
                    intent?.TargetGoldfishTurn is <= 6 ? 2 : 0),
                $"Combo signals: combo tags {comboTags}, tutors {tutors}, target turn {intent?.TargetGoldfishTurn?.ToString(CultureInfo.InvariantCulture) ?? "none"}."),
            Candidate(SimulationProfileIds.Control,
                Score(
                    ContainsAny(intentText, "control", "permission", "counterspell") ? 4 : 0,
                    Math.Min(5, interaction),
                    CountRole(included, DeckRoles.BoardWipes)),
                $"Control signals: interaction/wipes {interaction}."),
            Candidate(SimulationProfileIds.Stax,
                Score(
                    ContainsAny(intentText, "stax", "hatebear", "prison", "tax") ? 4 : 0,
                    Math.Min(6, stax)),
                $"Stax signals: stax tags {stax}."),
            Candidate(SimulationProfileIds.BigMana,
                Score(
                    ContainsAny(intentText, "big mana", "ramp", "landfall", "lands") ? 3 : 0,
                    ramp >= 12 ? 4 : ramp >= 9 ? 2 : 0,
                    Math.Min(3, averageManaValueCards)),
                $"Big-mana signals: ramp {ramp}, high mana value cards {averageManaValueCards}."),
            Candidate(SimulationProfileIds.Aggro,
                Score(
                    ContainsAny(intentText, "aggro", "combat", "voltron", "tokens") ? 3 : 0,
                    Math.Min(4, tokens),
                    Math.Min(3, voltron),
                    creatureCount >= 25 ? 2 : 0),
                $"Aggro signals: creatures {creatureCount}, token tags {tokens}, voltron tags {voltron}."),
            Candidate(SimulationProfileIds.Value,
                Score(
                    ContainsAny(intentText, "value", "midrange", "blink", "aristocrats", "reanimator", "spellslinger", "artifacts", "enchantress", "dungeon") ? 3 : 0,
                    Math.Min(3, engines),
                    Math.Min(2, blink),
                    Math.Min(2, reanimator),
                    draw >= 8 ? 1 : 0),
                $"Value signals: engines {engines}, blink {blink}, reanimation {reanimator}, draw {draw}."),
            Candidate(SimulationProfileIds.Neutral, 0, "Neutral fallback.")
        ];

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Creates one auto-profile candidate evidence row.
    /// </summary>
    private static SimulationProfileEvidence Candidate(string profileId, double score, string message)
    {
        return new SimulationProfileEvidence
        {
            Source = "auto-profile",
            ProfileId = profileId,
            Score = score,
            Message = message
        };
    }

    /// <summary>
    /// Sums deterministic profile signal scores.
    /// </summary>
    private static int Score(params int[] values)
    {
        return values.Sum();
    }

    /// <summary>
    /// Counts card quantities whose primary role matches a requested role.
    /// </summary>
    private static int CountRole(IEnumerable<DeckCard> cards, string role)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(role, StringComparison.OrdinalIgnoreCase))
            .Sum(card => Math.Max(1, card.Quantity));
    }

    /// <summary>
    /// Counts card quantities whose secondary tags include a requested tag.
    /// </summary>
    private static int CountTag(IEnumerable<DeckCard> cards, string tag)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Sum(card => Math.Max(1, card.Quantity));
    }

    /// <summary>
    /// Returns cards that are not in excluded workspace categories.
    /// </summary>
    private static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        HashSet<string> excluded = workspace.Categories
            .Where(category => !category.IncludedInDeck)
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return workspace.Cards.Where(card => !DeckCategoryOrdering.OrderedDistinct(
            DeckCategoryOrdering.PrimaryCategory(card),
            card.Categories).Any(excluded.Contains));
    }

    /// <summary>
    /// Gets a card snapshot or an empty snapshot when metadata is missing.
    /// </summary>
    private static CardSnapshot GetSnapshot(DeckCard card)
    {
        return card.Snapshot ?? new CardSnapshot();
    }

    /// <summary>
    /// Normalizes caller and intent profile values, including legacy aliases.
    /// </summary>
    private static string? NormalizeRequestedProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = NormalizeProfileId(value);
        return normalized switch
        {
            "commander-default" => SimulationProfileIds.Neutral,
            "midrange" => SimulationProfileIds.Value,
            "prison" => SimulationProfileIds.Stax,
            _ => normalized
        };
    }

    /// <summary>
    /// Normalizes a profile id token for dictionary lookup.
    /// </summary>
    private static string NormalizeProfileId(string value)
    {
        return DeckIntentVocabulary.NormalizeToken(value);
    }

    /// <summary>
    /// Adds a theme tag when it is not already present.
    /// </summary>
    private static void AddUnique(List<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    /// <summary>
    /// Checks whether a value contains any provided token.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Deep-clones a profile through the same JSON shape used for external profiles.
    /// </summary>
    private static SimulationProfile Clone(SimulationProfile profile)
    {
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        return JsonSerializer.Deserialize<SimulationProfile>(json, JsonOptions)
            ?? NeutralProfile();
    }

    /// <summary>
    /// Deep-clones a profile setting or route fragment through the external JSON shape.
    /// </summary>
    private static T Clone<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Unable to clone {typeof(T).Name}.");
    }

    /// <summary>
    /// Checks whether a deserialized settings object still matches its model defaults.
    /// </summary>
    private static bool IsDefaultSettings<T>(T value)
        where T : new()
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        string defaultJson = JsonSerializer.Serialize(new T(), JsonOptions);
        return json.Equals(defaultJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the small built-in Commander play-pattern profile catalog.
    /// </summary>
    private static List<SimulationProfile> BuiltInProfiles()
    {
        List<SimulationProfile> profiles =
        [
            new SimulationProfile
            {
                Id = SimulationProfileIds.Neutral,
                Name = "Neutral",
                Description = "Least-assumption Commander simulation with conservative fallback win detection.",
                ThemeTags = ["blink", "tokens", "aristocrats", "reanimator", "spellslinger", "voltron", "lands", "artifacts", "enchantress", "graveyard", "dungeon"],
                Sequencing = new SimulationSequencingSettings
                {
                    HoldInteractionFromTurn = 1,
                    MinimumInteractionHeld = 1,
                    DrawPriority = 1,
                    TutorPriority = 3,
                    ComboPriority = 3,
                    WinconPriority = 4,
                    DefaultPriority = 2,
                },
                WinDetection = new SimulationWinDetectionSettings
                {
                    AllowFallbackComboWins = false,
                    FinisherPressureThreshold = 10,
                    FinisherPowerThreshold = 22,
                    CombatPowerThreshold = 36,
                }
            },
            new SimulationProfile
            {
                Id = SimulationProfileIds.Aggro,
                Name = "Aggro",
                Description = "Prioritizes pressure, combat clock, protection, and tempo interaction.",
                ThemeTags = ["tokens", "voltron", "equipment", "auras", "extra-combats", "go-wide"],
                Sequencing = new SimulationSequencingSettings
                {
                    HoldInteractionFromTurn = 4,
                    MinimumInteractionHeld = 0,
                    DrawPriority = 2,
                    TutorPriority = 3,
                    ComboPriority = 3,
                    WinconPriority = 1,
                    DefaultPriority = 2,
                },
                Scenarios = new SimulationScenarioSettings
                {
                    CommanderTurn = 4,
                    ProtectionTurn = 4,
                    HateTurn = 3,
                    ColorTurn = 3,
                    InteractionTurn = 4,
                    ComboTurn = 6,
                },
                WinDetection = new SimulationWinDetectionSettings
                {
                    AllowFallbackComboWins = false,
                    FinisherPressureThreshold = 8,
                    FinisherPowerThreshold = 18,
                    CombatPowerThreshold = 30,
                }
            },
            new SimulationProfile
            {
                Id = SimulationProfileIds.Combo,
                Name = "Combo",
                Description = "Prioritizes route assembly, tutors, card selection, and protected wins.",
                ThemeTags = ["storm", "spellslinger", "blink", "aristocrats", "graveyard", "artifacts"],
                Mulligan = new SimulationMulliganSettings
                {
                    SevenCardFreeKeepScore = 7,
                    SevenCardKeepScore = 5.5,
                    SixCardKeepScore = 4,
                    EarlyRampWeight = 2,
                    EarlyDrawWeight = 1.25,
                    EarlyInteractionWeight = 0.75,
                    CommanderPlanWeight = 2.5,
                },
                Sequencing = new SimulationSequencingSettings
                {
                    HoldInteractionFromTurn = 3,
                    MinimumInteractionHeld = 1,
                    DrawPriority = 2,
                    TutorPriority = 1,
                    ComboPriority = 1,
                    WinconPriority = 4,
                    DefaultPriority = 3,
                },
                Scenarios = new SimulationScenarioSettings
                {
                    CommanderTurn = 3,
                    ProtectionTurn = 4,
                    HateTurn = 3,
                    ColorTurn = 2,
                    InteractionTurn = 3,
                    ComboTurn = 4,
                },
                WinDetection = new SimulationWinDetectionSettings
                {
                    AllowFallbackComboWins = true,
                    FallbackComboEarliestTurn = 5,
                    FinisherPressureThreshold = 8,
                    FinisherPowerThreshold = 18,
                    CombatPowerThreshold = 36,
                }
            },
            new SimulationProfile
            {
                Id = SimulationProfileIds.Control,
                Name = "Control",
                Description = "Prioritizes mana stability, draw, holding answers, sweepers, and late inevitability.",
                ThemeTags = ["counterspells", "pillowfort", "mill", "planeswalkers", "theft"],
                Mulligan = new SimulationMulliganSettings
                {
                    EarlyInteractionWeight = 1,
                    EarlyDrawWeight = 1.25,
                    CommanderPlanWeight = 1.25,
                },
                Sequencing = new SimulationSequencingSettings
                {
                    HoldInteractionFromTurn = 2,
                    MinimumInteractionHeld = 1,
                    DrawPriority = 1,
                    TutorPriority = 3,
                    ComboPriority = 3,
                    WinconPriority = 4,
                    DefaultPriority = 2,
                },
                Scenarios = new SimulationScenarioSettings
                {
                    CommanderTurn = 5,
                    ProtectionTurn = 5,
                    HateTurn = 3,
                    ColorTurn = 3,
                    InteractionTurn = 3,
                    ComboTurn = 6,
                }
            },
            new SimulationProfile
            {
                Id = SimulationProfileIds.Value,
                Name = "Value",
                Description = "Commander midrange simulation for engines, card advantage, and flexible interaction.",
                ThemeTags = ["blink", "aristocrats", "reanimator", "artifacts", "enchantress", "dungeon", "lands", "graveyard"],
                Sequencing = new SimulationSequencingSettings
                {
                    HoldInteractionFromTurn = 3,
                    MinimumInteractionHeld = 1,
                    DrawPriority = 1,
                    TutorPriority = 3,
                    ComboPriority = 3,
                    WinconPriority = 4,
                    DefaultPriority = 2,
                }
            },
            new SimulationProfile
            {
                Id = SimulationProfileIds.BigMana,
                Name = "Big Mana",
                Description = "Prioritizes ramp, land drops, large payoffs, and mana scaling.",
                ThemeTags = ["lands", "landfall", "ramp", "x-spells", "eldrazi"],
                Mulligan = new SimulationMulliganSettings
                {
                    EarlyRampWeight = 2.75,
                    CommanderPlanWeight = 1.5,
                },
                Sequencing = new SimulationSequencingSettings
                {
                    HoldInteractionFromTurn = 4,
                    MinimumInteractionHeld = 0,
                    DrawPriority = 2,
                    TutorPriority = 3,
                    ComboPriority = 3,
                    WinconPriority = 4,
                    DefaultPriority = 2,
                },
                Scenarios = new SimulationScenarioSettings
                {
                    CommanderTurn = 5,
                    ProtectionTurn = 5,
                    HateTurn = 3,
                    ColorTurn = 3,
                    InteractionTurn = 4,
                    ComboTurn = 6,
                }
            },
            new SimulationProfile
            {
                Id = SimulationProfileIds.Stax,
                Name = "Stax",
                Description = "Prioritizes early asymmetrical hate, parity-breaking, and slower deterministic clocks.",
                ThemeTags = ["hatebears", "prison", "tax", "pillowfort"],
                Mulligan = new SimulationMulliganSettings
                {
                    EarlyInteractionWeight = 1,
                    CommanderPlanWeight = 1.25,
                },
                Sequencing = new SimulationSequencingSettings
                {
                    HoldInteractionFromTurn = 3,
                    MinimumInteractionHeld = 1,
                    DrawPriority = 2,
                    TutorPriority = 2,
                    ComboPriority = 3,
                    WinconPriority = 4,
                    DefaultPriority = 1,
                },
                Scenarios = new SimulationScenarioSettings
                {
                    CommanderTurn = 4,
                    ProtectionTurn = 5,
                    HateTurn = 2,
                    ColorTurn = 3,
                    InteractionTurn = 3,
                    ComboTurn = 6,
                }
            }
        ];

        foreach (SimulationProfile profile in profiles)
        {
            profile.WinRoutes.AddRange(CommonCommanderRoutes());
        }

        return profiles;
    }

    /// <summary>
    /// Builds conservative cross-profile route templates for common Commander inevitability engines.
    /// </summary>
    private static List<SimulationRouteDefinition> CommonCommanderRoutes()
    {
        return
        [
            new SimulationRouteDefinition
            {
                Name = "Aristocrats Drain Clock",
                Kind = "aristocrats",
                EarliestTurn = 5,
                Source = "profile-common",
                Requirements = ["sac-outlet", "drain-payoff", "drain-clock", "turn>=5"],
            },
            new SimulationRouteDefinition
            {
                Name = "Enchantment Recursion Engine",
                Kind = "engine-inevitability",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["commander", "enchantment-recursion", "engine-payoff", "graveyard>=1", "turn>=6"],
            },
            new SimulationRouteDefinition
            {
                Name = "Enchantress Engine Plus Payoff",
                Kind = "engine-inevitability",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["enchantress-engine", "engine-payoff", "turn>=6"],
            },
            new SimulationRouteDefinition
            {
                Name = "Repeatable Graveyard Recursion",
                Kind = "engine-inevitability",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["repeatable-graveyard-recursion", "engine-payoff", "graveyard>=1", "turn>=6"],
            },
            new SimulationRouteDefinition
            {
                Name = "Treasure Alternate Win",
                Kind = "treasure-alt-win",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["treasure-engine", "treasure-payoff", "turn>=6"],
            },
            new SimulationRouteDefinition
            {
                Name = "Food Lifegain Drain Burst",
                Kind = "food-lifegain-drain",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["food-bank", "lifegain-burst", "lifegain-payoff", "drain-payoff", "turn>=6"],
            },
            new SimulationRouteDefinition
            {
                Name = "Food Artifact Leaves Drain",
                Kind = "food-artifact-drain",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["artifact-token-bank", "artifact-leaves-drain", "turn>=6"],
            },
            new SimulationRouteDefinition
            {
                Name = "Food Board Combat Alpha",
                Kind = "food-combat-alpha",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["food-bank", "food-combat-alpha", "turn>=6"],
            },
            new SimulationRouteDefinition
            {
                Name = "Commander Damage Pressure",
                Kind = "commander-damage",
                EarliestTurn = 6,
                Source = "profile-common",
                Requirements = ["commander", "commander-damage-pressure", "turn>=6"],
            },
        ];
    }
}
