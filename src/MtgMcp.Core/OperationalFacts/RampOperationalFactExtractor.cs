using System.Text.RegularExpressions;

namespace MtgMcp.Core;

/// <summary>
/// Extracts ramp operational facts from cached Scryfall text, produced-mana fields, and saved Tagger evidence.
/// </summary>
public static partial class RampOperationalFactExtractor
{
    /// <summary>
    /// Builds operational facts for one workspace card without consulting card-name overrides.
    /// </summary>
    public static CardOperationalFacts Extract(DeckCard card)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string oracleText = snapshot.OracleText ?? "";
        string typeLine = snapshot.TypeLine ?? "";
        List<string> taggerTags = ReadTaggerTags(card);
        CardOperationalFacts facts = new()
        {
            CardName = card.Name,
            Role = role.PrimaryRole,
        };

        bool taggerRamp = AddTaggerEvidence(taggerTags, facts);
        RampOperationalFacts? ramp = TryParseRamp(card, snapshot, typeLine, oracleText, facts);
        if (ramp is not null)
        {
            facts.Ramp = ramp;
        }
        else if (taggerRamp || role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            facts.Ramp = UnknownRampShape(card, snapshot);
            facts.Warnings.Add("unknownShape: ramp role evidence exists, but operational timing shape was not recognized.");
        }

        if (string.IsNullOrWhiteSpace(oracleText) && snapshot.ProducedMana.Count == 0)
        {
            facts.Warnings.Add("missingSourceData: cached oracle text and produced mana were unavailable.");
        }

        return facts;
    }

    /// <summary>
    /// Parses a recognized ramp shape from reusable Oracle-text and type-line patterns.
    /// </summary>
    private static RampOperationalFacts? TryParseRamp(
        DeckCard card,
        CardSnapshot snapshot,
        string typeLine,
        string oracleText,
        CardOperationalFacts facts)
    {
        int castMana = ManaValue(snapshot);
        string text = oracleText.Replace("\r\n", "\n", StringComparison.Ordinal);
        string activatedCost = TextBeforeFirstColon(text);
        bool requiresTap = activatedCost.Contains("{T}", StringComparison.OrdinalIgnoreCase);
        int activationMana = ParseGenericActivationMana(activatedCost);
        bool sacrificesSelf = ContainsAny(activatedCost, "sacrifice this")
            || SacrificeSelfRegex(card.Name).IsMatch(activatedCost);
        bool isPermanent = ContainsAny(typeLine, "Artifact", "Creature", "Enchantment");
        bool isCreature = ContainsAny(typeLine, "Creature");
        bool isArtifact = ContainsAny(typeLine, "Artifact");
        bool isInstantOrSorcery = ContainsAny(typeLine, "Instant", "Sorcery");
        bool searchesLandToBattlefield = ContainsAny(text, "search your library")
            && ContainsAny(text, "land card", "basic land", "forest card", "plains card", "island card", "swamp card", "mountain card")
            && ContainsAny(text, "onto the battlefield", "put that card onto the battlefield", "put them onto the battlefield");
        bool entersTapped = ContainsAny(text, "onto the battlefield tapped", "battlefield tapped", "enters the battlefield tapped");
        bool createsTreasure = ContainsAny(text, "treasure token") && ContainsAny(text, "create", "creates");
        bool costReducer = ContainsAny(text, "cost {1} less", "costs {1} less", "cost one less", "costs one less", "cost less to cast");
        List<string> producedMana = ReadProducedMana(snapshot, text);

        if (searchesLandToBattlefield && (activationMana > 0 || requiresTap || sacrificesSelf))
        {
            AddParserEvidence(facts, "activated-land-ramp", "Activated land-ramp pattern with post-cast cost.");
            return new RampOperationalFacts
            {
                Kind = isCreature ? "creatureSacrificeLandRamp" : "activatedLandRamp",
                CastMana = castMana,
                ActivationMana = activationMana,
                RequiresTap = requiresTap,
                SacrificesSelf = sacrificesSelf,
                Destination = "battlefield",
                EntersTapped = entersTapped,
                EarliestManaGainTurn = ActivatedLandRampTurn(castMana, activationMana, requiresTap, entersTapped),
                OneShot = true,
                Repeatable = false,
                ProducedMana = producedMana,
            };
        }

        if (searchesLandToBattlefield)
        {
            AddParserEvidence(facts, "spell-land-ramp", "Land-ramp spell pattern that puts a land onto the battlefield.");
            return new RampOperationalFacts
            {
                Kind = entersTapped ? "spellLandRampTapped" : "spellLandRampUntapped",
                CastMana = castMana,
                ActivationMana = 0,
                RequiresTap = false,
                SacrificesSelf = false,
                Destination = "battlefield",
                EntersTapped = entersTapped,
                EarliestManaGainTurn = entersTapped ? castMana + 1 : Math.Max(1, castMana),
                OneShot = true,
                Repeatable = false,
                ProducedMana = producedMana,
            };
        }

        if (producedMana.Count > 0 && isPermanent)
        {
            AddParserEvidence(facts, "permanent-mana-source", "Permanent with produced-mana or Add-mana text.");
            return new RampOperationalFacts
            {
                Kind = isCreature ? "manaDork" : isArtifact ? "manaRock" : "permanentManaSource",
                CastMana = castMana,
                ActivationMana = 0,
                RequiresTap = requiresTap || ContainsAny(text, "{T}: Add", "tap: add"),
                SacrificesSelf = false,
                Destination = "manaPool",
                EntersTapped = ContainsAny(text, "enters the battlefield tapped"),
                EarliestManaGainTurn = PermanentManaTurn(castMana, isCreature, ContainsAny(text, "enters the battlefield tapped")),
                OneShot = false,
                Repeatable = true,
                ProducedMana = producedMana,
            };
        }

        if (createsTreasure)
        {
            AddParserEvidence(facts, "treasure-ramp", "Treasure-token pattern.");
            return new RampOperationalFacts
            {
                Kind = isPermanent ? "treasureMaker" : "treasureBurst",
                CastMana = castMana,
                ActivationMana = 0,
                RequiresTap = false,
                SacrificesSelf = !isPermanent,
                Destination = "manaPool",
                EntersTapped = false,
                EarliestManaGainTurn = Math.Max(1, castMana),
                OneShot = !isPermanent,
                Repeatable = isPermanent,
                ProducedMana = ["W", "U", "B", "R", "G"],
            };
        }

        if (isInstantOrSorcery && ContainsAny(text, "add {", "add one mana", "add two mana", "add three mana"))
        {
            AddParserEvidence(facts, "ritual-ramp", "Instant or sorcery Add-mana pattern.");
            return new RampOperationalFacts
            {
                Kind = "ritual",
                CastMana = castMana,
                ActivationMana = 0,
                RequiresTap = false,
                SacrificesSelf = true,
                Destination = "manaPool",
                EntersTapped = false,
                EarliestManaGainTurn = Math.Max(1, castMana),
                OneShot = true,
                Repeatable = false,
                ProducedMana = producedMana,
            };
        }

        if (costReducer)
        {
            AddParserEvidence(facts, "cost-reducer", "Cost-reduction pattern.");
            return new RampOperationalFacts
            {
                Kind = "costReducer",
                CastMana = castMana,
                ActivationMana = 0,
                RequiresTap = false,
                SacrificesSelf = false,
                Destination = "costReduction",
                EntersTapped = false,
                EarliestManaGainTurn = castMana + 1,
                OneShot = false,
                Repeatable = true,
                ProducedMana = [],
            };
        }

        return null;
    }

    /// <summary>
    /// Creates an unknown ramp shape while keeping role evidence visible.
    /// </summary>
    private static RampOperationalFacts UnknownRampShape(DeckCard card, CardSnapshot snapshot)
    {
        return new RampOperationalFacts
        {
            Kind = "unknownShape",
            CastMana = ManaValue(snapshot),
            ActivationMana = 0,
            Destination = "unknown",
            EntersTapped = null,
            EarliestManaGainTurn = null,
            ProducedMana = snapshot.ProducedMana.ToList(),
        };
    }

    /// <summary>
    /// Adds source-backed evidence for saved Scryfall Tagger ramp tags.
    /// </summary>
    private static bool AddTaggerEvidence(List<string> taggerTags, CardOperationalFacts facts)
    {
        bool ramp = false;
        foreach (string tag in taggerTags)
        {
            if (!DeckTaggerTaxonomy.TryGetRule(tag, out DeckTaggerRule rule))
            {
                continue;
            }

            if (rule.Role.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
            {
                ramp = true;
                facts.Evidence.Add(new CardFactEvidence
                {
                    Source = CardFacetSourceNames.Tagger,
                    Kind = "sourceBacked",
                    Label = rule.Slug,
                    Detail = $"Saved Scryfall Tagger oracle tag maps to {DeckRoles.Ramp}."
                });
            }
        }

        return ramp;
    }

    /// <summary>
    /// Adds parser-derived evidence for a recognized shape.
    /// </summary>
    private static void AddParserEvidence(CardOperationalFacts facts, string label, string detail)
    {
        facts.Evidence.Add(new CardFactEvidence
        {
            Source = "oracle-parser",
            Kind = "parserDerived",
            Label = label,
            Detail = detail
        });
    }

    /// <summary>
    /// Reads Tagger oracle-tag annotations from workspace metadata.
    /// </summary>
    private static List<string> ReadTaggerTags(DeckCard card)
    {
        if (!card.Metadata.TryGetValue(CardFacetNames.TaggerOracleTags, out string? value)
            || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([',', ';', '|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Reads produced mana from normalized Scryfall fields or obvious Add-mana Oracle text.
    /// </summary>
    private static List<string> ReadProducedMana(CardSnapshot snapshot, string oracleText)
    {
        if (snapshot.ProducedMana.Count > 0)
        {
            return snapshot.ProducedMana.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (ContainsAny(oracleText, "any color", "commander's color identity", "mana of any color"))
        {
            return ["W", "U", "B", "R", "G"];
        }

        List<string> symbols = [];
        foreach (Match match in ManaSymbolRegex().Matches(oracleText))
        {
            string symbol = match.Groups["symbol"].Value;
            if (PerformanceMana.ColoredSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase)
                || symbol.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                symbols.Add(symbol);
            }
        }

        return symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reads the text before the first activated-ability colon.
    /// </summary>
    private static string TextBeforeFirstColon(string oracleText)
    {
        int colon = oracleText.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? "" : oracleText[..colon];
    }

    /// <summary>
    /// Parses generic activation mana in an ability cost.
    /// </summary>
    private static int ParseGenericActivationMana(string activationCost)
    {
        int total = 0;
        foreach (Match match in GenericManaRegex().Matches(activationCost))
        {
            if (int.TryParse(match.Groups["mana"].Value, out int value))
            {
                total += value;
            }
        }

        return total;
    }

    /// <summary>
    /// Reads a nonnegative integer mana value from cached Scryfall data.
    /// </summary>
    private static int ManaValue(CardSnapshot snapshot)
    {
        return Math.Max(0, (int)Math.Ceiling(snapshot.ManaValue ?? 0));
    }

    /// <summary>
    /// Estimates when activated land ramp first increases usable mana.
    /// </summary>
    private static int ActivatedLandRampTurn(int castMana, int activationMana, bool requiresTap, bool entersTapped)
    {
        int turn = Math.Max(1, castMana);
        if (activationMana > 0 || requiresTap)
        {
            turn++;
        }

        if (entersTapped)
        {
            turn++;
        }

        return Math.Max(1, turn);
    }

    /// <summary>
    /// Estimates when a permanent mana source first increases usable mana.
    /// </summary>
    private static int PermanentManaTurn(int castMana, bool isCreature, bool entersTapped)
    {
        if (castMana == 0 && !isCreature && !entersTapped)
        {
            return 1;
        }

        return Math.Max(1, castMana + 1);
    }

    /// <summary>
    /// Checks whether text contains any supplied phrase.
    /// </summary>
    private static bool ContainsAny(string value, params ReadOnlySpan<string> needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches mana symbols that represent produced mana.
    /// </summary>
    [GeneratedRegex(@"\{(?<symbol>[WUBRGC])\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManaSymbolRegex();

    /// <summary>
    /// Matches generic mana symbols in activation costs.
    /// </summary>
    [GeneratedRegex(@"\{(?<mana>\d+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex GenericManaRegex();

    /// <summary>
    /// Builds a self-sacrifice regex for card names that contain punctuation.
    /// </summary>
    private static Regex SacrificeSelfRegex(string cardName)
    {
        return new Regex(
            $@"\bsacrifice\s+{Regex.Escape(cardName)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }
}
