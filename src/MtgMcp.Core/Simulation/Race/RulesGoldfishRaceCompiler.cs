using System.Globalization;
using System.Text.RegularExpressions;

namespace MtgMcp.Core;

/// <summary>
/// Compiles workspace card snapshots into conservative goldfish race templates.
/// </summary>
public static partial class RulesGoldfishRaceCompiler
{
    /// <summary>
    /// Compiles included workspace cards for the race kernel.
    /// </summary>
    public static RulesGoldfishRaceDeck CompileDeck(DeckWorkspace workspace, string label)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        RulesGoldfishRaceDeck result = new()
        {
            Label = label,
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
        };
        foreach (DeckCard card in DeckCategoryInclusion.IncludedCards(workspace))
        {
            RulesGoldfishRaceCard template = CompileCard(card, result.Warnings);
            if (IsCommandZoneCard(card))
            {
                result.CommandZoneCards.Add(template);
            }
            else
            {
                result.Cards.Add(template);
            }
        }

        return result;
    }

    /// <summary>
    /// Compiles one workspace card into a conservative race template.
    /// </summary>
    private static RulesGoldfishRaceCard CompileCard(DeckCard card, List<string> warnings)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        string typeLine = SnapshotText(snapshot.TypeLine, snapshot.Faces.Select(face => face.TypeLine));
        string oracleText = SnapshotText(snapshot.OracleText, snapshot.Faces.Select(face => face.OracleText));
        string normalizedText = Normalize(oracleText);
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        bool isLand = role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            || Contains(typeLine, "Land");
        bool isCreature = Contains(typeLine, "Creature");
        RulesGoldfishRaceCard template = new()
        {
            Name = card.Name,
            Quantity = Math.Max(0, card.Quantity),
            ManaValue = SafeManaValue(snapshot.ManaValue),
            IsLand = isLand,
            IsCreature = isCreature,
            StaysOnBattlefield = isCreature || IsPermanentType(typeLine),
            Power = ReadPower(card, snapshot, isCreature, warnings),
            Toughness = ReadToughness(snapshot),
        };

        template.ManaProduced = ReadManaProduced(card, snapshot, isLand, isCreature, normalizedText, warnings);
        template.ManaSourceIsCreature = isCreature && template.ManaProduced > 0;
        ApplyDrawTemplate(template, normalizedText);
        ApplyRampTemplate(template, normalizedText);
        ApplyTokenTemplate(template, normalizedText);
        ApplyLifeLossTemplate(template, normalizedText);
        ApplyCombatPayoffTemplate(template, role, normalizedText);
        WarnUnsupportedTemplate(card, template, normalizedText, warnings);
        return template;
    }

    /// <summary>
    /// Checks whether the card should start outside the library.
    /// </summary>
    private static bool IsCommandZoneCard(DeckCard card)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        return DeckDefaults.IsCommanderCategory(primaryCategory)
            || DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the first useful snapshot string, falling back to face text.
    /// </summary>
    private static string SnapshotText(string? primary, IEnumerable<string?> faces)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        return string.Join(
            " ",
            faces.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
    }

    /// <summary>
    /// Checks whether the type line represents a permanent spell.
    /// </summary>
    private static bool IsPermanentType(string typeLine)
    {
        return Contains(typeLine, "Artifact")
            || Contains(typeLine, "Enchantment")
            || Contains(typeLine, "Planeswalker")
            || Contains(typeLine, "Battle");
    }

    /// <summary>
    /// Converts mana value to a bounded integer cost.
    /// </summary>
    private static int SafeManaValue(double? manaValue)
    {
        if (!manaValue.HasValue)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Ceiling(manaValue.Value), 0, 20);
    }

    /// <summary>
    /// Reads creature power, warning when dynamic stats must be bounded.
    /// </summary>
    private static int ReadPower(
        DeckCard card,
        CardSnapshot snapshot,
        bool isCreature,
        List<string> warnings)
    {
        if (TryReadInt(snapshot.Power, out int power))
        {
            return Math.Max(0, power);
        }

        foreach (CardFaceSnapshot face in snapshot.Faces)
        {
            if (TryReadInt(face.Power, out power))
            {
                return Math.Max(0, power);
            }
        }

        if (isCreature)
        {
            AddWarning(warnings, $"{card.Name}: creature power is missing or dynamic; race uses conservative power 1.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Reads creature toughness when available.
    /// </summary>
    private static int ReadToughness(CardSnapshot snapshot)
    {
        if (TryReadInt(snapshot.Toughness, out int toughness))
        {
            return Math.Max(0, toughness);
        }

        foreach (CardFaceSnapshot face in snapshot.Faces)
        {
            if (TryReadInt(face.Toughness, out toughness))
            {
                return Math.Max(0, toughness);
            }
        }

        return 0;
    }

    /// <summary>
    /// Reads reusable mana production.
    /// </summary>
    private static int ReadManaProduced(
        DeckCard card,
        CardSnapshot snapshot,
        bool isLand,
        bool isCreature,
        string normalizedText,
        List<string> warnings)
    {
        if (snapshot.ProducedMana.Count > 0)
        {
            return 1;
        }

        if (isLand && IsBasicLandName(card.Name))
        {
            return 1;
        }

        if (!isLand && ProducesManaFromText(normalizedText))
        {
            return 1;
        }

        if (isLand)
        {
            AddWarning(warnings, $"{card.Name}: land has no producedMana snapshot; race treats it as non-mana-producing.");
        }
        else if (isCreature && Contains(normalizedText, "add "))
        {
            AddWarning(warnings, $"{card.Name}: ambiguous mana creature text was not compiled as reusable mana.");
        }

        return 0;
    }

    /// <summary>
    /// Applies simple card-draw templates.
    /// </summary>
    private static void ApplyDrawTemplate(RulesGoldfishRaceCard template, string text)
    {
        if (!Contains(text, "draw"))
        {
            return;
        }

        if (ContainsAny(text, "draw two cards", "draw 2 cards"))
        {
            template.DrawCards = Math.Max(template.DrawCards, 2);
        }
        else if (ContainsAny(text, "draw a card", "draw one card", "draw 1 card"))
        {
            template.DrawCards = Math.Max(template.DrawCards, 1);
        }
    }

    /// <summary>
    /// Applies simple ramp templates.
    /// </summary>
    private static void ApplyRampTemplate(RulesGoldfishRaceCard template, string text)
    {
        if (ContainsAny(
            text,
            "search your library for a basic land",
            "search your library for up to one basic land",
            "put a land card from your hand onto the battlefield"))
        {
            template.RampLands = Math.Max(template.RampLands, 1);
        }
    }

    /// <summary>
    /// Applies simple token templates.
    /// </summary>
    private static void ApplyTokenTemplate(RulesGoldfishRaceCard template, string text)
    {
        Match match = TokenRegex().Match(text);
        if (!match.Success)
        {
            return;
        }

        template.CreateTokens = Math.Max(template.CreateTokens, ReadSmallNumber(match.Groups["count"].Value, defaultValue: 1));
        template.TokenPower = ReadSmallNumber(match.Groups["power"].Value, defaultValue: 1);
        template.TokenToughness = ReadSmallNumber(match.Groups["toughness"].Value, defaultValue: 1);
    }

    /// <summary>
    /// Applies simple opponent life-loss templates.
    /// </summary>
    private static void ApplyLifeLossTemplate(RulesGoldfishRaceCard template, string text)
    {
        Match match = LifeLossRegex().Match(text);
        if (match.Success)
        {
            template.LifeLoss = Math.Max(template.LifeLoss, ReadSmallNumber(match.Groups["amount"].Value, defaultValue: 1));
        }
    }

    /// <summary>
    /// Applies deterministic combat payoff templates.
    /// </summary>
    private static void ApplyCombatPayoffTemplate(
        RulesGoldfishRaceCard template,
        CardRoleAssignment role,
        string text)
    {
        bool payoff = role.Tags.Contains(DeckTags.CombatPayoff, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(text, "battle cry", "melee", "creatures you control get +", "attacking creatures you control have double strike");
        if (!payoff)
        {
            return;
        }

        template.IsCombatPayoff = true;
        template.StaysOnBattlefield = true;
        if (Contains(text, "attacking creatures you control have double strike"))
        {
            template.GrantsTeamDoubleStrike = true;
        }

        if (ContainsAny(text, "creatures you control have haste", "creatures you control gain haste"))
        {
            template.GrantsTeamHaste = true;
        }

        int pump = ReadTeamPump(text);
        if (ContainsAny(text, "battle cry", "melee"))
        {
            pump = Math.Max(pump, 1);
        }

        template.TeamPowerBonus = Math.Max(template.TeamPowerBonus, pump);
    }

    /// <summary>
    /// Adds warnings for text that the v1 compiler does not model.
    /// </summary>
    private static void WarnUnsupportedTemplate(
        DeckCard card,
        RulesGoldfishRaceCard template,
        string text,
        List<string> warnings)
    {
        if (template.IsLand || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        bool recognizedEffect = template.ManaProduced > 0
            || template.DrawCards > 0
            || template.RampLands > 0
            || template.CreateTokens > 0
            || template.LifeLoss > 0
            || template.IsCombatPayoff;
        if (!recognizedEffect)
        {
            AddWarning(warnings, $"{card.Name}: unsupported or ambiguous rules text ignored by conservative race compiler.");
        }
    }

    /// <summary>
    /// Adds a bounded warning.
    /// </summary>
    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count < 40 && !warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add(warning);
        }
    }

    /// <summary>
    /// Checks for text-driven mana production.
    /// </summary>
    private static bool ProducesManaFromText(string text)
    {
        return ContainsAny(text, "{t}: add", "tap: add", "add one mana", "add {c}", "add one mana of any color");
    }

    /// <summary>
    /// Reads a team power bonus from a pump phrase.
    /// </summary>
    private static int ReadTeamPump(string text)
    {
        Match match = TeamPumpRegex().Match(text);
        return match.Success
            ? ReadSmallNumber(match.Groups["amount"].Value, defaultValue: 1)
            : 0;
    }

    /// <summary>
    /// Reads a small number from digits or common English words.
    /// </summary>
    private static int ReadSmallNumber(string value, int defaultValue)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            return Math.Clamp(number, 0, 20);
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Tries to read an integer stat.
    /// </summary>
    private static bool TryReadInt(string? value, out int number)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>
    /// Checks whether a name is a basic land with implicit mana production.
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
    /// Normalizes text for simple phrase matching.
    /// </summary>
    private static string Normalize(string value)
    {
        return value.Replace('\u2212', '-').ToLowerInvariant();
    }

    /// <summary>
    /// Checks whether text contains a phrase.
    /// </summary>
    private static bool Contains(string value, string needle)
    {
        return value.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether text contains any phrase.
    /// </summary>
    private static bool ContainsAny(string value, params ReadOnlySpan<string> needles)
    {
        foreach (string needle in needles)
        {
            if (Contains(value, needle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches common token creation phrases.
    /// </summary>
    [GeneratedRegex(@"create (?<count>a|an|one|two|three|four|five|\d+) (?<power>\d+)/(?<toughness>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>
    /// Matches simple opponent life-loss phrases.
    /// </summary>
    [GeneratedRegex(@"(?:each opponent|target opponent|each player) loses (?<amount>one|two|three|four|five|\d+) life", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LifeLossRegex();

    /// <summary>
    /// Matches simple team pump phrases.
    /// </summary>
    [GeneratedRegex(@"creatures you control get \+(?<amount>\d+)/\+\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TeamPumpRegex();
}
