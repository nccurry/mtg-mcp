using System.Globalization;

namespace MtgMcp.Core;

/// <summary>
/// Parses and formats human-readable deck intent sections.
/// </summary>
public static partial class DeckIntentText
{
    /// <summary>
    /// Stores the section title.
    /// </summary>
    public const string Title = "MTG MCP Deck Intent";

    /// <summary>
    /// Stores the section end marker.
    /// </summary>
    public const string EndMarker = "End MTG MCP Deck Intent";

    /// <summary>
    /// Extracts an intent section from a description.
    /// </summary>
    public static DeckIntentResult Extract(string? description, string workspaceId = "")
    {
        string plainText = ToPlainText(description);
        if (!TryFindBlock(plainText, out int start, out int end))
        {
            return new DeckIntentResult
            {
                WorkspaceId = workspaceId,
                Found = false,
                Source = "description"
            };
        }

        string intentText = plainText[start..end].Trim();
        DeckIntentResult result = Parse(intentText);
        result.WorkspaceId = workspaceId;
        result.Found = true;
        result.IntentText = intentText;
        result.Source = "description";
        return result;
    }

    /// <summary>
    /// Parses intent text.
    /// </summary>
    public static DeckIntentResult Parse(string intentText)
    {
        DeckIntent intent = new();
        DeckIntentResult result = new()
        {
            Found = !string.IsNullOrWhiteSpace(intentText),
            Intent = intent,
            IntentText = intentText.Trim(),
            Source = "description"
        };

        string section = "";
        foreach (string rawLine in SplitLines(intentText))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Equals(Title, StringComparison.OrdinalIgnoreCase)
                || line.Equals(EndMarker, StringComparison.OrdinalIgnoreCase))
            {
                section = "";
                continue;
            }

            if (TryReadSection(line, out string? nextSection))
            {
                section = nextSection;
                continue;
            }

            if (line.StartsWith('-'))
            {
                AddListItem(intent, section, line[1..].Trim());
                continue;
            }

            if (TrySplitKeyValue(line, out string key, out string value))
            {
                ApplyValue(intent, section, key, value, result.Warnings);
            }
        }

        return result;
    }

    /// <summary>
    /// Formats a deck intent section.
    /// </summary>
    public static string Format(DeckIntent intent)
    {
        List<string> lines =
        [
            Title,
            $"Version: {Math.Max(1, intent.Version)}"
        ];

        AddValue(lines, "Format", intent.Format);
        AddValue(lines, "Commander", intent.Commander);
        AddValue(lines, "Goal", intent.Goal);
        AddValue(lines, "Archetype", intent.Archetype);
        AddValue(lines, "Power Level", intent.PowerLevel);
        AddValue(lines, "Power Target", intent.PowerTarget);
        AddValue(lines, "Heuristic Profile", intent.HeuristicProfile);
        AddValue(lines, "Simulation Profile", intent.SimulationProfile);
        AddValue(lines, "Package Template", intent.PackageTemplate);
        AddDelimitedValue(lines, "Archetype Tags", intent.ArchetypeTags);
        AddValue(lines, "Target Goldfish Turn", intent.TargetGoldfishTurn?.ToString(CultureInfo.InvariantCulture));
        AddDelimitedValue(lines, "Local Meta", intent.LocalMeta);

        string? budget = FormatBudget(intent.Budget);
        AddValue(lines, "Budget", budget);

        Dictionary<string, DeckIntentTarget> buildTargets = intent.BuildTargets.Count > 0
            ? intent.BuildTargets
            : intent.Targets;
        if (buildTargets.Count > 0)
        {
            lines.Add("");
            lines.Add("Build Targets");
            foreach (KeyValuePair<string, DeckIntentTarget> target in buildTargets.OrderBy(target => target.Key))
            {
                lines.Add($"{target.Key}: {FormatTarget(target.Value)}");
            }
        }

        if (intent.Packages.Count > 0)
        {
            lines.Add("");
            lines.Add("Packages");
            foreach (KeyValuePair<string, DeckIntentTarget> package in intent.Packages.OrderBy(package => package.Key))
            {
                lines.Add($"{package.Key}: {FormatTarget(package.Value)}");
            }
        }

        if (intent.Priorities is not null)
        {
            lines.Add("");
            lines.Add("Priorities");
            lines.Add($"Role Fit: {intent.Priorities.Role.ToString("0.##", CultureInfo.InvariantCulture)}");
            lines.Add($"Power: {intent.Priorities.Power.ToString("0.##", CultureInfo.InvariantCulture)}");
            lines.Add($"Price: {intent.Priorities.Price.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        if (HasSimulationSettings(intent.Simulation))
        {
            lines.Add("");
            lines.Add("Simulation");
            AddSimulationValue(lines, "Commander Dependency", intent.Simulation.CommanderDependency);
            AddSimulationValue(lines, "Mulligan Style", intent.Simulation.MulliganStyle);
            AddSimulationValue(lines, "Hold Interaction From Turn", intent.Simulation.HoldInteractionFromTurn?.ToString(CultureInfo.InvariantCulture));
            AddSimulationValue(lines, "Minimum Interaction Held", intent.Simulation.MinimumInteractionHeld?.ToString(CultureInfo.InvariantCulture));
            AddSimulationValue(lines, "Prefer Commander On Curve", intent.Simulation.PreferCommanderOnCurve?.ToString());
            AddSimulationValue(lines, "Accept Shield Down Win Attempt", intent.Simulation.AcceptShieldDownWinAttempt?.ToString());
            foreach (KeyValuePair<string, string> value in intent.Simulation.Values.OrderBy(value => value.Key))
            {
                if (IsKnownSimulationKey(value.Key))
                {
                    continue;
                }

                lines.Add($"{value.Key}: {value.Value}");
            }
        }

        if (intent.WinRoutes.Count > 0)
        {
            lines.Add("");
            lines.Add("Win Routes");
            foreach (DeckIntentWinRoute route in intent.WinRoutes)
            {
                List<string> parts = [];
                if (route.Requirements.Count > 0)
                {
                    parts.Add($"requires {string.Join(", ", route.Requirements)}");
                }

                if (route.EarliestTurn.HasValue)
                {
                    parts.Add($"earliest turn {route.EarliestTurn.Value}");
                }

                if (!string.IsNullOrWhiteSpace(route.Kind))
                {
                    parts.Add($"kind {route.Kind}");
                }

                lines.Add($"{route.Name}: {string.Join("; ", parts)}");
            }
        }

        AddList(lines, "Prefer", intent.Prefer);
        AddList(lines, "Avoid", intent.Avoid);
        AddList(lines, "Protect", intent.Protect);
        lines.Add(EndMarker);
        return string.Join(Environment.NewLine, lines).Trim();
    }

    /// <summary>
    /// Updates or appends the intent section in a description.
    /// </summary>
    public static string UpsertDescription(string? description, string intentText, bool forceQuillDelta = false)
    {
        string normalizedIntent = NormalizeIntentBlock(intentText);
        if (TryUpsertQuillDescription(description, normalizedIntent, out string quillDescription))
        {
            return quillDescription;
        }

        string plainText = ToPlainText(description).TrimEnd();
        string updatedText;

        if (TryFindBlock(plainText, out int start, out int end))
        {
            updatedText = string.Concat(
                plainText[..start].TrimEnd(),
                Environment.NewLine,
                Environment.NewLine,
                normalizedIntent,
                Environment.NewLine,
                Environment.NewLine,
                plainText[end..].TrimStart()
            ).Trim();
        }
        else if (plainText.Length == 0)
        {
            updatedText = normalizedIntent;
        }
        else
        {
            updatedText = string.Concat(
                plainText,
                Environment.NewLine,
                Environment.NewLine,
                normalizedIntent
            );
        }

        return FromPlainText(updatedText, forceQuillDelta || IsQuillDelta(description));
    }

    /// <summary>
    /// Removes the intent section from a description.
    /// </summary>
    public static string ClearDescription(string? description, bool forceQuillDelta = false)
    {
        if (TryClearQuillDescription(description, out string quillDescription))
        {
            return quillDescription;
        }

        string plainText = ToPlainText(description).TrimEnd();
        if (!TryFindBlock(plainText, out int start, out int end))
        {
            return forceQuillDelta
                ? FromPlainText(plainText, asQuillDelta: true)
                : description ?? "";
        }

        string updatedText = string.Concat(
            plainText[..start].TrimEnd(),
            Environment.NewLine,
            Environment.NewLine,
            plainText[end..].TrimStart()
        ).Trim();
        return FromPlainText(updatedText, forceQuillDelta || IsQuillDelta(description));
    }


    /// <summary>
    /// Normalizes user-provided intent text to a bounded section.
    /// </summary>
    private static string NormalizeIntentBlock(string intentText)
    {
        string text = intentText.Trim();
        if (!text.StartsWith(Title, StringComparison.OrdinalIgnoreCase))
        {
            text = Title + Environment.NewLine + text;
        }

        if (!text.Contains(EndMarker, StringComparison.OrdinalIgnoreCase))
        {
            text += Environment.NewLine + EndMarker;
        }

        return text;
    }

    /// <summary>
    /// Splits text into lines.
    /// </summary>
    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
    }

    /// <summary>
    /// Reads a section heading.
    /// </summary>
    private static bool TryReadSection(string line, out string section)
    {
        section = NormalizeKey(line.TrimEnd(':'));
        return section is "targets"
            or "buildtargets"
            or "packages"
            or "simulation"
            or "winroutes"
            or "prefer"
            or "avoid"
            or "protect"
            or "priorities"
            or "localmeta";
    }

    /// <summary>
    /// Splits a key value line.
    /// </summary>
    private static bool TrySplitKeyValue(string line, out string key, out string value)
    {
        int colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            key = "";
            value = "";
            return false;
        }

        key = line[..colon].Trim();
        value = line[(colon + 1)..].Trim();
        return key.Length > 0;
    }

    /// <summary>
    /// Applies a parsed value to the intent.
    /// </summary>
    private static void ApplyValue(
        DeckIntent intent,
        string section,
        string key,
        string value,
        List<string> warnings
    )
    {
        string normalizedKey = NormalizeKey(key);
        if (section == "targets")
        {
            intent.Targets[NormalizeRoleName(key)] = ParseTarget(value);
            return;
        }

        if (section == "buildtargets")
        {
            DeckIntentTarget target = ParseTarget(value);
            intent.BuildTargets[NormalizeRoleName(key)] = target;
            intent.Targets[NormalizeRoleName(key)] = target;
            return;
        }

        if (section == "packages")
        {
            intent.Packages[NormalizeRoleName(key)] = ParseTarget(value);
            return;
        }

        if (section == "simulation")
        {
            ApplySimulationValue(intent.Simulation, normalizedKey, key, value, warnings);
            return;
        }

        if (section == "winroutes")
        {
            intent.WinRoutes.Add(ParseWinRoute(key, value, warnings));
            return;
        }

        if (section == "localmeta")
        {
            AddDelimitedItems(intent.LocalMeta, value);
            return;
        }

        if (section == "priorities")
        {
            intent.Priorities ??= new ReplacementWeights();
            ApplyPriority(intent.Priorities, normalizedKey, value, warnings);
            return;
        }

        switch (normalizedKey)
        {
            case "version":
                intent.Version = TryParseInt(value) ?? 1;
                break;
            case "format":
                intent.Format = EmptyToNull(value);
                break;
            case "commander":
                intent.Commander = EmptyToNull(value);
                break;
            case "goal":
                intent.Goal = EmptyToNull(value);
                break;
            case "archetype":
                intent.Archetype = EmptyToNull(value);
                break;
            case "powerlevel":
                intent.PowerLevel = NormalizePowerLevel(value, warnings);
                break;
            case "powertarget":
                intent.PowerTarget = EmptyToNull(value);
                break;
            case "heuristicprofile":
                intent.HeuristicProfile = NormalizeHeuristicProfile(value, warnings);
                break;
            case "simulationprofile":
                intent.SimulationProfile = NormalizeSimulationProfile(value, warnings);
                break;
            case "packagetemplate":
                intent.PackageTemplate = NormalizePackageTemplate(value, warnings);
                break;
            case "archetypetags":
                AddDelimitedItems(intent.ArchetypeTags, value);
                break;
            case "targetgoldfishturn":
                intent.TargetGoldfishTurn = TryParseInt(value);
                if (!intent.TargetGoldfishTurn.HasValue)
                {
                    warnings.Add("Target Goldfish Turn was not an integer.");
                }

                break;
            case "localmeta":
                AddDelimitedItems(intent.LocalMeta, value);
                break;
            case "budget":
                intent.Budget.Text = EmptyToNull(value);
                intent.Budget.MaxCardPrice = TryParseMoney(value);
                intent.Budget.PreferCheaperSwaps = ContainsAny(value, "prefer cheaper", "cheaper swaps", "budget");
                break;
            default:
                warnings.Add($"Unknown intent field '{key}' was ignored.");
                break;
        }
    }

    /// <summary>
    /// Applies a parsed priority value.
    /// </summary>
    private static void ApplyPriority(
        ReplacementWeights weights,
        string key,
        string value,
        List<string> warnings
    )
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            warnings.Add($"Priority '{key}' was not a number.");
            return;
        }

        switch (key)
        {
            case "rolefit":
            case "role":
                weights.Role = parsed;
                break;
            case "power":
                weights.Power = parsed;
                break;
            case "price":
                weights.Price = parsed;
                break;
            default:
                warnings.Add($"Unknown priority '{key}' was ignored.");
                break;
        }
    }

    /// <summary>
    /// Adds a parsed list item to the intent.
    /// </summary>
    private static void AddListItem(DeckIntent intent, string section, string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        switch (section)
        {
            case "prefer":
                AddUnique(intent.Prefer, item);
                break;
            case "avoid":
                AddUnique(intent.Avoid, item);
                break;
            case "protect":
                AddUnique(intent.Protect, item);
                break;
            case "localmeta":
                AddDelimitedItems(intent.LocalMeta, item);
                break;
        }
    }

    /// <summary>
    /// Parses a target range.
    /// </summary>
    private static DeckIntentTarget ParseTarget(string value)
    {
        string text = value.Trim();
        string[] range = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (range.Length == 2)
        {
            return Target(text, TryParseInt(range[0]), TryParseInt(range[1]));
        }

        int? number = TryParseInt(text.TrimEnd('+'));
        if (number.HasValue && text.EndsWith('+'))
        {
            return Target(text, number, null);
        }

        return Target(text, number, number);
    }

    /// <summary>
    /// Creates a target instance.
    /// </summary>
    private static DeckIntentTarget Target(string raw, int? minimum, int? maximum)
    {
        return new DeckIntentTarget
        {
            Raw = raw,
            Minimum = minimum,
            Maximum = maximum
        };
    }

    /// <summary>
    /// Formats a budget line.
    /// </summary>
    private static string? FormatBudget(DeckIntentBudget budget)
    {
        if (!string.IsNullOrWhiteSpace(budget.Text))
        {
            return budget.Text;
        }

        if (budget.MaxCardPrice.HasValue)
        {
            return $"avoid cards over ${budget.MaxCardPrice.Value.ToString("0.##", CultureInfo.InvariantCulture)} unless they are core";
        }

        return null;
    }

    /// <summary>
    /// Formats a target line.
    /// </summary>
    private static string FormatTarget(DeckIntentTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.Raw))
        {
            return target.Raw;
        }

        return target.Minimum == target.Maximum
            ? target.Minimum?.ToString(CultureInfo.InvariantCulture) ?? ""
            : $"{target.Minimum}-{target.Maximum}";
    }

    /// <summary>
    /// Adds a scalar value when present.
    /// </summary>
    private static void AddValue(List<string> lines, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{name}: {value}");
        }
    }

    /// <summary>
    /// Adds a comma-delimited value when present.
    /// </summary>
    private static void AddDelimitedValue(List<string> lines, string name, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
        {
            lines.Add($"{name}: {string.Join(", ", values)}");
        }
    }

    /// <summary>
    /// Adds one simulation setting when present.
    /// </summary>
    private static void AddSimulationValue(List<string> lines, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{name}: {value}");
        }
    }

    /// <summary>
    /// Adds a named list when present.
    /// </summary>
    private static void AddList(List<string> lines, string name, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        lines.Add("");
        lines.Add(name);
        foreach (string value in values)
        {
            lines.Add($"- {value}");
        }
    }

    /// <summary>
    /// Normalizes a key for matching.
    /// </summary>
    private static string NormalizeKey(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a role target name.
    /// </summary>
    private static string NormalizeRoleName(string value)
    {
        string normalized = value.Trim();
        return normalized.Equals("wipes", StringComparison.OrdinalIgnoreCase)
            ? DeckRoles.BoardWipes
            : normalized;
    }

    /// <summary>
    /// Normalizes a power level or keeps the raw value with a warning.
    /// </summary>
    private static string? NormalizePowerLevel(string value, List<string> warnings)
    {
        return NormalizeVocabularyValue(
            value,
            warnings,
            DeckIntentVocabulary.TryNormalizePowerLevel,
            "Power Level",
            DeckIntentVocabulary.PowerLevels);
    }

    /// <summary>
    /// Normalizes a heuristic profile or keeps the raw value with a warning.
    /// </summary>
    private static string? NormalizeHeuristicProfile(string value, List<string> warnings)
    {
        return NormalizeVocabularyValue(
            value,
            warnings,
            DeckIntentVocabulary.TryNormalizeHeuristicProfile,
            "Heuristic Profile",
            DeckIntentVocabulary.HeuristicProfiles);
    }

    /// <summary>
    /// Normalizes a simulation profile or keeps the raw value with a warning.
    /// </summary>
    private static string? NormalizeSimulationProfile(string value, List<string> warnings)
    {
        return NormalizeVocabularyValue(
            value,
            warnings,
            DeckIntentVocabulary.TryNormalizeSimulationProfile,
            "Simulation Profile",
            DeckIntentVocabulary.SimulationProfiles);
    }

    /// <summary>
    /// Normalizes a package template or keeps the raw value with a warning.
    /// </summary>
    private static string? NormalizePackageTemplate(string value, List<string> warnings)
    {
        return NormalizeVocabularyValue(
            value,
            warnings,
            DeckIntentVocabulary.TryNormalizePackageTemplate,
            "Package Template",
            DeckIntentVocabulary.PackageTemplates);
    }

    /// <summary>
    /// Normalizes a known vocabulary value.
    /// </summary>
    private static string? NormalizeVocabularyValue(
        string value,
        List<string> warnings,
        TryNormalizeVocabulary normalize,
        string fieldName,
        IReadOnlyList<string> supportedValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (normalize(value, out string normalized))
        {
            return normalized;
        }

        warnings.Add($"{fieldName} '{value}' is not one of the documented values: {string.Join(", ", supportedValues)}.");
        return value.Trim();
    }

    /// <summary>
    /// Parses an integer from text.
    /// </summary>
    private static int? TryParseInt(string value)
    {
        string digits = new(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Parses a boolean-like value from text.
    /// </summary>
    private static bool? TryParseBool(string value)
    {
        if (bool.TryParse(value, out bool parsed))
        {
            return parsed;
        }

        string normalized = DeckIntentVocabulary.NormalizeToken(value);
        return normalized switch
        {
            "yes" or "y" or "true" or "on" => true,
            "no" or "n" or "false" or "off" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Parses the first money-like value from text.
    /// </summary>
    private static decimal? TryParseMoney(string value)
    {
        string token = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.Contains('$', StringComparison.Ordinal) || decimal.TryParse(part.Trim('$'), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            ?? "";
        token = token.Trim().TrimStart('$').TrimEnd(',', '.', ';');
        return decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Converts blank text to null.
    /// </summary>
    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Adds a string only when it is not already present.
    /// </summary>
    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(value);
        }
    }

    /// <summary>
    /// Adds comma- or semicolon-delimited values.
    /// </summary>
    private static void AddDelimitedItems(List<string> values, string text)
    {
        foreach (string value in text.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            AddUnique(values, value);
        }
    }

    /// <summary>
    /// Applies one simulation setting line.
    /// </summary>
    private static void ApplySimulationValue(
        DeckIntentSimulationSettings settings,
        string normalizedKey,
        string key,
        string value,
        List<string> warnings)
    {
        settings.Values[key.Trim()] = value.Trim();
        switch (normalizedKey)
        {
            case "commanderdependency":
                settings.CommanderDependency = EmptyToNull(value);
                break;
            case "mulliganstyle":
                settings.MulliganStyle = EmptyToNull(value);
                break;
            case "holdinteractionfromturn":
                settings.HoldInteractionFromTurn = TryParseInt(value);
                if (!settings.HoldInteractionFromTurn.HasValue)
                {
                    warnings.Add("Simulation field 'Hold Interaction From Turn' was not an integer.");
                }

                break;
            case "minimuminteractionheld":
                settings.MinimumInteractionHeld = TryParseInt(value);
                if (!settings.MinimumInteractionHeld.HasValue)
                {
                    warnings.Add("Simulation field 'Minimum Interaction Held' was not an integer.");
                }

                break;
            case "prefercommanderoncurve":
                settings.PreferCommanderOnCurve = TryParseBool(value);
                if (!settings.PreferCommanderOnCurve.HasValue)
                {
                    warnings.Add("Simulation field 'Prefer Commander On Curve' was not true or false.");
                }

                break;
            case "acceptshielddownwinattempt":
                settings.AcceptShieldDownWinAttempt = TryParseBool(value);
                if (!settings.AcceptShieldDownWinAttempt.HasValue)
                {
                    warnings.Add("Simulation field 'Accept Shield Down Win Attempt' was not true or false.");
                }

                break;
        }
    }

    /// <summary>
    /// Parses a deck-local win route line.
    /// </summary>
    private static DeckIntentWinRoute ParseWinRoute(string name, string value, List<string> warnings)
    {
        DeckIntentWinRoute route = new()
        {
            Name = name.Trim(),
            Raw = value.Trim()
        };
        foreach (string part in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("requires ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string requirement in part["requires ".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    route.Requirements.Add(requirement);
                    if (!SimulationRouteEvaluator.IsSupportedRequirement(requirement))
                    {
                        warnings.Add($"Win route '{route.Name}' has unsupported requirement '{requirement}'.");
                    }
                }

                continue;
            }

            if (part.StartsWith("earliest turn ", StringComparison.OrdinalIgnoreCase))
            {
                route.EarliestTurn = TryParseInt(part["earliest turn ".Length..]);
                if (!route.EarliestTurn.HasValue)
                {
                    warnings.Add($"Win route '{route.Name}' earliest turn was not an integer.");
                }

                continue;
            }

            if (part.StartsWith("kind ", StringComparison.OrdinalIgnoreCase))
            {
                route.Kind = part["kind ".Length..].Trim();
                continue;
            }

            warnings.Add($"Win route '{route.Name}' ignored unknown clause '{part}'.");
        }

        return route;
    }

    /// <summary>
    /// Checks whether a normalized simulation key is formatted explicitly elsewhere.
    /// </summary>
    private static bool IsKnownSimulationKey(string key)
    {
        string normalized = NormalizeKey(key);
        return normalized is "commanderdependency"
            or "mulliganstyle"
            or "holdinteractionfromturn"
            or "minimuminteractionheld"
            or "prefercommanderoncurve"
            or "acceptshielddownwinattempt";
    }

    /// <summary>
    /// Checks whether simulation settings contain any explicit value.
    /// </summary>
    private static bool HasSimulationSettings(DeckIntentSimulationSettings settings)
    {
        return settings.Values.Count > 0
            || !string.IsNullOrWhiteSpace(settings.CommanderDependency)
            || !string.IsNullOrWhiteSpace(settings.MulliganStyle)
            || settings.HoldInteractionFromTurn.HasValue
            || settings.MinimumInteractionHeld.HasValue
            || settings.PreferCommanderOnCurve.HasValue
            || settings.AcceptShieldDownWinAttempt.HasValue;
    }

    /// <summary>
    /// Checks whether text contains one of the provided terms.
    /// </summary>
    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tries to normalize an intent vocabulary value.
    /// </summary>
    private delegate bool TryNormalizeVocabulary(string value, out string normalized);
}
