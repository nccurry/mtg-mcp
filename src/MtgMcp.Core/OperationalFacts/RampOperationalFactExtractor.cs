using System.Text.RegularExpressions;

namespace MtgMcp.Core;

/// <summary>
/// Extracts supported operational facts from cached Scryfall text, produced-mana fields, and saved Tagger evidence.
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

        AddTaggerEvidence(
            taggerTags,
            facts,
            out bool taggerRamp,
            out bool taggerDraw,
            out bool taggerInteraction);
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

        DrawOperationalFacts? draw = TryParseDraw(snapshot, typeLine, oracleText, facts);
        if (draw is not null)
        {
            facts.Draw = draw;
        }
        else if (taggerDraw || role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase))
        {
            facts.Draw = UnknownDrawShape(snapshot);
            facts.Warnings.Add("unknownShape: draw role evidence exists, but operational draw shape was not recognized.");
        }

        InteractionOperationalFacts? interaction = TryParseInteraction(snapshot, typeLine, oracleText, facts);
        if (interaction is not null)
        {
            facts.Interaction = interaction;
        }
        else if (taggerInteraction || IsInteractionRole(role.PrimaryRole))
        {
            facts.Interaction = UnknownInteractionShape(snapshot);
            facts.Warnings.Add("unknownShape: interaction role evidence exists, but operational answer shape was not recognized.");
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
    /// Parses a recognized draw or card-selection shape from Oracle text.
    /// </summary>
    private static DrawOperationalFacts? TryParseDraw(
        CardSnapshot snapshot,
        string typeLine,
        string oracleText,
        CardOperationalFacts facts)
    {
        string text = oracleText.Replace("\r\n", "\n", StringComparison.Ordinal);
        bool drawsCards = ContainsAny(text, "draw a card", "draw two cards", "draw three cards", "draw cards", "draw X cards");
        bool impulseDraw = ContainsAny(text, "exile the top", "exile cards from the top")
            && ContainsAny(text, "you may play", "you may cast");
        bool selectionOnly = !drawsCards
            && ContainsAny(text, "scry", "surveil", "look at the top", "reveal the top");
        if (!drawsCards && !impulseDraw && !selectionOnly)
        {
            return null;
        }

        bool permanent = ContainsAny(typeLine, "Artifact", "Creature", "Enchantment", "Planeswalker");
        bool repeatable = permanent
            && ContainsAny(text, "whenever", "at the beginning", "{T}", "tap:");
        bool discardsCards = ContainsAny(text, "discard a card", "discard your hand", "then discard", "as an additional cost");
        bool conditional = repeatable || ContainsAny(text, "whenever", "if ", "when ", "at the beginning", "unless");
        int immediateCards = selectionOnly ? 0 : EstimateImmediateDrawCount(text, impulseDraw);
        AddParserEvidence(facts, selectionOnly ? "card-selection" : "card-draw", "Draw or card-selection pattern.");
        return new DrawOperationalFacts
        {
            Kind = selectionOnly
                ? "cardSelection"
                : repeatable
                    ? "repeatableDraw"
                    : impulseDraw
                        ? "impulseDraw"
                        : discardsCards
                            ? "looting"
                            : immediateCards >= 3
                                ? "largeDraw"
                                : "cardDraw",
            CastMana = ManaValue(snapshot),
            ImmediateCards = immediateCards,
            Repeatable = repeatable,
            SelectionOnly = selectionOnly,
            DiscardsCards = discardsCards,
            ImpulseDraw = impulseDraw,
            Conditional = conditional,
            InstantSpeed = ContainsAny(typeLine, "Instant") || (permanent && ContainsAny(text, ":") && !ContainsAny(text, "activate only as a sorcery")),
        };
    }

    /// <summary>
    /// Parses a recognized removal, counterspell, board-wipe, or protection shape from Oracle text.
    /// </summary>
    private static InteractionOperationalFacts? TryParseInteraction(
        CardSnapshot snapshot,
        string typeLine,
        string oracleText,
        CardOperationalFacts facts)
    {
        string text = oracleText.Replace("\r\n", "\n", StringComparison.Ordinal);
        bool counterspell = ContainsAny(text, "counter target spell", "counter target activated", "counter target triggered");
        bool boardWide = ContainsAny(text, "destroy all", "exile all", "return all", "each opponent sacrifices", "each player sacrifices")
            || ContainsAny(text, "damage to each creature", "damage to all creatures");
        bool permanentAnswer = ContainsAny(text, "destroy target", "exile target", "return target")
            || ContainsAny(text, "damage to target", "any target");
        bool protection = ContainsAny(text, "indestructible until", "gain hexproof", "gains hexproof", "protection from", "prevent all damage", "phase out");
        bool graveyardHate = ContainsAny(text, "exile target card from a graveyard", "exile all graveyards", "exile target player's graveyard");
        if (!counterspell && !boardWide && !permanentAnswer && !protection && !graveyardHate)
        {
            return null;
        }

        List<string> targets = InteractionTargets(text, counterspell, boardWide, graveyardHate);
        AddParserEvidence(facts, "interaction", "Removal, counterspell, board-wipe, graveyard-hate, or protection pattern.");
        return new InteractionOperationalFacts
        {
            Kind = boardWide
                ? "boardWideAnswer"
                : counterspell
                    ? "stackAnswer"
                    : protection
                        ? "protection"
                        : graveyardHate
                            ? "graveyardHate"
                            : "singleTargetAnswer",
            CastMana = ManaValue(snapshot),
            InstantSpeed = ContainsAny(typeLine, "Instant") || ContainsAny(text, "flash") || counterspell || protection,
            StackInteraction = counterspell,
            BoardWide = boardWide,
            PermanentAnswer = permanentAnswer || boardWide,
            Protection = protection,
            Modal = ContainsAny(text, "choose one", "choose two", "choose up to"),
            Targets = targets,
        };
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
    /// Creates an unknown draw shape while keeping role evidence visible.
    /// </summary>
    private static DrawOperationalFacts UnknownDrawShape(CardSnapshot snapshot)
    {
        return new DrawOperationalFacts
        {
            Kind = "unknownShape",
            CastMana = ManaValue(snapshot),
            Conditional = true,
        };
    }

    /// <summary>
    /// Creates an unknown interaction shape while keeping role evidence visible.
    /// </summary>
    private static InteractionOperationalFacts UnknownInteractionShape(CardSnapshot snapshot)
    {
        return new InteractionOperationalFacts
        {
            Kind = "unknownShape",
            CastMana = ManaValue(snapshot),
        };
    }

    /// <summary>
    /// Adds source-backed evidence for supported saved Scryfall Tagger tags.
    /// </summary>
    private static void AddTaggerEvidence(
        List<string> taggerTags,
        CardOperationalFacts facts,
        out bool ramp,
        out bool draw,
        out bool interaction)
    {
        ramp = false;
        draw = false;
        interaction = false;
        foreach (string tag in taggerTags)
        {
            if (!DeckTaggerTaxonomy.TryGetRule(tag, out DeckTaggerRule rule))
            {
                continue;
            }

            if (rule.Role.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
            {
                ramp = true;
            }
            else if (rule.Role.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase))
            {
                draw = true;
            }
            else if (IsInteractionRole(rule.Role))
            {
                interaction = true;
            }
            else
            {
                continue;
            }

            facts.Evidence.Add(new CardFactEvidence
            {
                Source = CardFacetSourceNames.Tagger,
                Kind = "sourceBacked",
                Label = rule.Slug,
                Detail = $"Saved Scryfall Tagger oracle tag maps to {rule.Role}."
            });
        }
    }

    /// <summary>
    /// Estimates the immediate cards gained by common draw phrases.
    /// </summary>
    private static int EstimateImmediateDrawCount(string oracleText, bool impulseDraw)
    {
        if (ContainsAny(oracleText, "draw seven cards", "draw seven"))
        {
            return 7;
        }

        if (ContainsAny(oracleText, "draw three cards", "draw three"))
        {
            return 3;
        }

        if (ContainsAny(oracleText, "draw two cards", "draw two"))
        {
            return 2;
        }

        if (ContainsAny(oracleText, "draw a card", "draw one card"))
        {
            return 1;
        }

        if (ContainsAny(oracleText, "draw cards equal", "draw X cards"))
        {
            return 3;
        }

        return impulseDraw ? 2 : 1;
    }

    /// <summary>
    /// Reads coarse answer target labels from interaction text.
    /// </summary>
    private static List<string> InteractionTargets(string oracleText, bool counterspell, bool boardWide, bool graveyardHate)
    {
        List<string> targets = [];
        AddTarget(targets, "spell", counterspell || ContainsAny(oracleText, "target spell"));
        AddTarget(targets, "creature", ContainsAny(oracleText, "target creature", "all creatures", "each creature"));
        AddTarget(targets, "artifact", ContainsAny(oracleText, "target artifact", "artifacts"));
        AddTarget(targets, "enchantment", ContainsAny(oracleText, "target enchantment", "enchantments"));
        AddTarget(targets, "planeswalker", ContainsAny(oracleText, "target planeswalker", "planeswalkers"));
        AddTarget(targets, "permanent", ContainsAny(oracleText, "target permanent", "nonland permanent", "all permanents"));
        AddTarget(targets, "graveyard", graveyardHate);
        AddTarget(targets, "board", boardWide);
        return targets;
    }

    /// <summary>
    /// Adds a target label when a parser condition matches.
    /// </summary>
    private static void AddTarget(List<string> targets, string target, bool condition)
    {
        if (!condition)
        {
            return;
        }

        foreach (string existing in targets)
        {
            if (existing.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        targets.Add(target);
    }

    /// <summary>
    /// Checks whether the role is treated as interaction by the evaluator.
    /// </summary>
    private static bool IsInteractionRole(string role)
    {
        return role.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            || role.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase)
            || role.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase);
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
