using System.Globalization;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Parses and formats human-readable deck intent sections.
/// </summary>
public static class DeckIntentText
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
        AddValue(lines, "Archetype", intent.Archetype);
        AddValue(lines, "Power Level", intent.PowerLevel);

        string? budget = FormatBudget(intent.Budget);
        AddValue(lines, "Budget", budget);

        if (intent.Targets.Count > 0)
        {
            lines.Add("");
            lines.Add("Targets");
            foreach (KeyValuePair<string, DeckIntentTarget> target in intent.Targets.OrderBy(target => target.Key))
            {
                lines.Add($"{target.Key}: {FormatTarget(target.Value)}");
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

        AddList(lines, "Prefer", intent.Prefer);
        AddList(lines, "Avoid", intent.Avoid);
        AddList(lines, "Protect", intent.Protect);
        lines.Add(EndMarker);
        return string.Join(Environment.NewLine, lines).Trim();
    }

    /// <summary>
    /// Updates or appends the intent section in a description.
    /// </summary>
    public static string UpsertDescription(string? description, string intentText)
    {
        string plainText = ToPlainText(description).TrimEnd();
        string normalizedIntent = NormalizeIntentBlock(intentText);
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

        return FromPlainText(updatedText, IsQuillDelta(description));
    }

    /// <summary>
    /// Removes the intent section from a description.
    /// </summary>
    public static string ClearDescription(string? description)
    {
        string plainText = ToPlainText(description).TrimEnd();
        if (!TryFindBlock(plainText, out int start, out int end))
        {
            return description ?? "";
        }

        string updatedText = string.Concat(
            plainText[..start].TrimEnd(),
            Environment.NewLine,
            Environment.NewLine,
            plainText[end..].TrimStart()
        ).Trim();
        return FromPlainText(updatedText, IsQuillDelta(description));
    }

    /// <summary>
    /// Converts Archidekt description storage to plain text.
    /// </summary>
    public static string ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "";
        }

        if (!IsQuillDelta(description))
        {
            return description;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(description);
            if (!document.RootElement.TryGetProperty("ops", out JsonElement ops)
                || ops.ValueKind != JsonValueKind.Array)
            {
                return description;
            }

            List<string> parts = [];
            foreach (JsonElement op in ops.EnumerateArray())
            {
                if (!op.TryGetProperty("insert", out JsonElement insert))
                {
                    continue;
                }

                if (insert.ValueKind == JsonValueKind.String)
                {
                    parts.Add(insert.GetString() ?? "");
                }
                else
                {
                    parts.Add(insert.GetRawText());
                }
            }

            return string.Concat(parts);
        }
        catch (JsonException)
        {
            return description;
        }
    }

    /// <summary>
    /// Converts plain text back to an Archidekt-compatible description shape.
    /// </summary>
    public static string FromPlainText(string plainText, bool asQuillDelta)
    {
        if (!asQuillDelta)
        {
            return plainText;
        }

        string text = plainText.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? plainText
            : plainText + Environment.NewLine;
        object delta = new
        {
            ops = new[]
            {
                new { insert = text }
            }
        };
        return JsonSerializer.Serialize(delta);
    }

    /// <summary>
    /// Creates a starter intent for a workspace.
    /// </summary>
    public static DeckIntent Suggest(DeckWorkspace workspace)
    {
        DeckIntent intent = new()
        {
            Format = workspace.Format,
            Commander = FindCommander(workspace),
            Archetype = SuggestArchetype(workspace),
            PowerLevel = "tuned-casual",
            Budget = new DeckIntentBudget
            {
                Text = "prefer cheaper swaps unless a card is core",
                PreferCheaperSwaps = true
            }
        };

        intent.Targets[DeckRoles.Lands] = Target("36-37", 36, 37);
        intent.Targets[DeckRoles.Ramp] = Target("8-10", 8, 10);
        intent.Targets[DeckRoles.Draw] = Target("9-11", 9, 11);
        intent.Targets[DeckRoles.Interaction] = Target("10-14", 10, 14);
        intent.Targets[DeckRoles.BoardWipes] = Target("2-4", 2, 4);
        intent.Priorities = new ReplacementWeights();
        intent.Prefer.AddRange(SuggestPreferences(workspace));
        intent.Avoid.AddRange(["infinite combos", "hard stax"]);
        intent.Protect.AddRange(SuggestProtectedCards(workspace));
        return intent;
    }

    /// <summary>
    /// Determines whether text looks like a Quill delta.
    /// </summary>
    private static bool IsQuillDelta(string? description)
    {
        string text = description?.Trim() ?? "";
        return text.StartsWith('{')
            && text.Contains("\"ops\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the intent block range.
    /// </summary>
    private static bool TryFindBlock(string text, out int start, out int end)
    {
        start = -1;
        end = -1;
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        int normalizedStart = normalized.IndexOf(Title, StringComparison.OrdinalIgnoreCase);
        if (normalizedStart < 0)
        {
            return false;
        }

        int normalizedEnd = normalized.IndexOf(EndMarker, normalizedStart, StringComparison.OrdinalIgnoreCase);
        normalizedEnd = normalizedEnd < 0
            ? normalized.Length
            : normalizedEnd + EndMarker.Length;
        start = ToOriginalIndex(text, normalizedStart);
        end = ToOriginalIndex(text, normalizedEnd);
        return true;
    }

    /// <summary>
    /// Converts a normalized line-ending index to the original string.
    /// </summary>
    private static int ToOriginalIndex(string original, int normalizedIndex)
    {
        int originalIndex = 0;
        int currentNormalized = 0;
        while (originalIndex < original.Length && currentNormalized < normalizedIndex)
        {
            if (original[originalIndex] == '\r'
                && originalIndex + 1 < original.Length
                && original[originalIndex + 1] == '\n')
            {
                originalIndex += 2;
                currentNormalized++;
                continue;
            }

            originalIndex++;
            currentNormalized++;
        }

        return originalIndex;
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
        return section is "targets" or "prefer" or "avoid" or "protect" or "priorities";
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
            case "archetype":
                intent.Archetype = EmptyToNull(value);
                break;
            case "powerlevel":
                intent.PowerLevel = EmptyToNull(value);
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
    /// Finds the commander card.
    /// </summary>
    private static string? FindCommander(DeckWorkspace workspace)
    {
        return workspace.Cards
            .FirstOrDefault(card =>
                string.Equals(card.PrimaryCategory, DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)
                || (card.Categories ?? []).Any(category => category.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)))
            ?.Name;
    }

    /// <summary>
    /// Suggests a broad archetype from tags and categories.
    /// </summary>
    private static string SuggestArchetype(DeckWorkspace workspace)
    {
        string text = string.Join(' ', workspace.Categories.Select(category => category.Name));
        if (workspace.Cards.Any(card => DeckRoleClassifier.Classify(card).Tags.Contains(DeckTags.Discard, StringComparer.OrdinalIgnoreCase))
            || ContainsAny(text, "discard"))
        {
            return "discard-control";
        }

        if (ContainsAny(text, "aristocrats", "death", "sacrifice"))
        {
            return "aristocrats";
        }

        return "synergy";
    }

    /// <summary>
    /// Suggests preference lines from the current deck.
    /// </summary>
    private static IEnumerable<string> SuggestPreferences(DeckWorkspace workspace)
    {
        string archetype = SuggestArchetype(workspace);
        if (archetype == "discard-control")
        {
            return ["repeatable discard", "discard payoffs", "cards that work without the commander"];
        }

        if (archetype == "aristocrats")
        {
            return ["death triggers", "sacrifice outlets", "recursive threats"];
        }

        return ["role fit", "mana efficiency", "cards that support the current plan"];
    }

    /// <summary>
    /// Suggests protected cards.
    /// </summary>
    private static IEnumerable<string> SuggestProtectedCards(DeckWorkspace workspace)
    {
        List<string> protectedCards = [];
        string? commander = FindCommander(workspace);
        if (!string.IsNullOrWhiteSpace(commander))
        {
            protectedCards.Add(commander);
        }

        return protectedCards;
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
    /// Checks whether text contains one of the provided terms.
    /// </summary>
    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
