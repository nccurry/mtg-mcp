using System.Text.RegularExpressions;

namespace MtgMcp.Core;

/// <summary>
/// Classifies cards into deck roles and tags.
/// </summary>
public static partial class DeckRoleClassifier
{
    /// <summary>
    /// Classifies the card.
    /// </summary>
    public static CardRoleAssignment Classify(DeckCard card)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        string categoryText = string.Join(' ', Categories(card).Append(primaryCategory));
        string typeLine = Text(snapshot.TypeLine, card.Metadata, "typeLine");
        string primaryTypeLine = PrimaryTypeLine(typeLine);
        bool hasNonPrimaryLandFace = HasNonPrimaryLandFace(typeLine);
        string oracleText = Text(snapshot.OracleText, card.Metadata, "oracleText");
        List<string> taggerOracleTags = AnnotationValues(card.Metadata, CardFacetNames.TaggerOracleTags);
        bool allowBoardWipeCategoryFallback = AllowsBoardWipeCategoryFallback(snapshot, oracleText, taggerOracleTags);
        string boardWipeCategoryText = allowBoardWipeCategoryFallback ? categoryText : "";
        string combined = $"{card.Name} {categoryText} {typeLine} {oracleText}";

        List<string> tags = ClassifyTags(card, combined);
        AddCanonicalTaggerTags(tags, taggerOracleTags);
        List<string> functionalRoles = ClassifyFunctionalRoles(
            categoryText,
            oracleText,
            snapshot,
            hasNonPrimaryLandFace);

        if (primaryCategory.Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase))
        {
            return Assignment(DeckRoles.Maybeboard, tags, 0.95);
        }

        if (primaryCategory.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)
            || primaryCategory.Equals("Commander", StringComparison.OrdinalIgnoreCase))
        {
            return Assignment(DeckRoles.Commander, tags, 0.95);
        }

        if (HasPrimaryLandCategory(card) || ContainsAny(primaryTypeLine, "land"))
        {
            return Assignment(DeckRoles.Lands, tags, 0.93);
        }

        CardRoleAssignment? taggerAssignment = TryClassifyFromTaggerTags(taggerOracleTags, tags);
        if (taggerAssignment is not null)
        {
            AddFunctionalRoles(taggerAssignment.FunctionalRoles, functionalRoles);
            return taggerAssignment;
        }

        if (ContainsAny(boardWipeCategoryText, DeckRoles.BoardWipes, "wipe")
            || ContainsBoardWipeText(oracleText))
        {
            return Assignment(DeckRoles.BoardWipes, tags, 0.82, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Ramp)
            || (snapshot.ProducedMana.Count > 0 && !hasNonPrimaryLandFace)
            || ContainsRampText(oracleText, hasNonPrimaryLandFace))
        {
            AddTag(tags, DeckTags.ManaFixing, snapshot.ProducedMana.Count > 1 || ContainsAny(oracleText, "mana of any color", "any color"));
            return Assignment(DeckRoles.Ramp, tags, 0.85, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Draw)
            || ContainsDrawText(oracleText))
        {
            return Assignment(DeckRoles.Draw, tags, 0.82, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Tutors)
            || ContainsAny(oracleText, "search your library", "searches your library"))
        {
            return Assignment(DeckRoles.Tutors, tags, 0.84, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Interaction, "removal")
            || ContainsAny(
                oracleText,
                "destroy target",
                "exile target",
                "counter target",
                "target creature gets",
                "fight target",
                "deals damage to target",
                "each opponent sacrifices",
                "target opponent sacrifices",
                "sacrifices a creature",
                "sacrifices an enchantment",
                "sacrifices an artifact"))
        {
            return Assignment(DeckRoles.Interaction, tags, 0.78, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Protection)
            || ContainsCommanderProtectionText(oracleText))
        {
            return Assignment(DeckRoles.Protection, tags, 0.78, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Recursion)
            || ContainsAny(oracleText, "return target", "from your graveyard", "from a graveyard to the battlefield", "return a creature card"))
        {
            return Assignment(DeckRoles.Recursion, tags, 0.76, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Wincons, "finisher")
            || ContainsFinisherText(oracleText))
        {
            return Assignment(DeckRoles.Wincons, tags, 0.72, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Payoffs, "payoff")
            || ContainsAny(oracleText, "whenever you discard", "whenever you draw", "whenever one or more", "whenever a creature dies"))
        {
            return Assignment(DeckRoles.Payoffs, tags, 0.68, functionalRoles);
        }

        if (ContainsAny(categoryText, DeckRoles.Synergy)
            || ContainsAny(combined, "synergy", "engine", "combo"))
        {
            return Assignment(DeckRoles.Synergy, tags, 0.62, functionalRoles);
        }

        return Assignment(DeckRoles.Utility, tags, tags.Count > 0 ? 0.55 : 0.35, functionalRoles);
    }

    /// <summary>
    /// Checks whether the card matches a role, tag, or category target.
    /// </summary>
    public static bool MatchesTarget(DeckCard card, string target)
    {
        CardRoleAssignment assignment = Classify(card);
        if (assignment.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (assignment.Tags.Any(tag => tag.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (assignment.FunctionalRoles.Any(role => role.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (Categories(card).Any(category => category.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return DeckCategoryOrdering.PrimaryCategory(card).Equals(target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a provider-neutral catalog search request for a role.
    /// </summary>
    public static CardSearchRequest SearchRequestForRole(string role, string? format, decimal? maxPrice = null)
    {
        return CardSearchRequest.ForRole(role, format, maxPrice);
    }

    /// <summary>
    /// Classifies secondary tags.
    /// </summary>
    private static List<string> ClassifyTags(DeckCard card, string text)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        List<string> tags = [];
        AddTag(tags, DeckTags.Discard, ContainsAny(text, "discard"));
        AddTag(tags, DeckTags.SacOutlet, ContainsAny(text, "sacrifice a creature:", "sacrifice another", "sacrifice an artifact:", "sacrifice a permanent:"));
        AddTag(tags, DeckTags.Aristocrats, ContainsAny(text, "whenever a creature dies", "whenever another creature dies", "dies, each opponent"));
        AddTag(tags, DeckTags.Tokens, ContainsAny(text, "create", "token"));
        AddTag(tags, DeckTags.ArtifactTokens, ContainsArtifactTokenText(text));
        AddTag(tags, DeckTags.Food, ContainsFoodText(text));
        AddTag(tags, DeckTags.Reanimation, ContainsGraveyardRecursionText(text));
        AddTag(tags, DeckTags.GraveyardHate, ContainsGraveyardHateText(text));
        AddTag(tags, DeckTags.Stax, ContainsAny(text, "can't cast", "can't attack", "doesn't untap", "skip", "players can't"));
        AddTag(tags, DeckTags.ComboPiece, ContainsAny(text, "combo", "untap") && ContainsAny(text, "add", "whenever", "copy"));
        AddTag(tags, DeckTags.CardSelection, ContainsAny(text, "scry", "surveil", "look at the top", "reveal the top"));
        AddTag(tags, DeckTags.Lifegain, ContainsAny(text, "gain life", "lifelink") || ContainsFoodText(text));
        AddTag(tags, DeckTags.Drain, ContainsAny(text, "opponent loses") && ContainsAny(text, "you gain"));
        AddTag(tags, DeckTags.Voltron, ContainsAny(text, "equipment", "equip", "aura", "enchanted creature"));
        AddTag(tags, DeckTags.Blink, ContainsAny(text, "exile") && ContainsAny(text, "return it", "return that card", "under its owner's control"));
        AddTag(tags, DeckTags.Mill, ContainsAny(text, "mill"));
        AddTag(tags, DeckTags.Politics, ContainsAny(text, "goad", "vote", "monarch", "tempting offer"));
        AddTag(tags, DeckTags.TableInteraction, ContainsAny(text, "each opponent", "each player", "each creature", "all creatures", "any number of target"));
        AddTag(tags, DeckTags.GoWideProtection, ContainsAny(text, "prevent all combat damage", "creatures can't attack you", "attacks you") && ContainsAny(text, "each creature", "creatures", "combat damage"));
        AddTag(tags, DeckTags.Pillowfort, ContainsAny(text, "creatures can't attack you", "can't attack you", "unless their controller pays", "prevent all combat damage"));
        AddTag(tags, DeckTags.TokenHate, ContainsAny(text, "destroy all tokens", "creature tokens", "tokens get", "tokens can't", "each creature gets -1/-1"));
        AddTag(tags, DeckTags.ArtifactEnchantmentHate, ContainsArtifactEnchantmentHateText(text));
        AddTag(tags, DeckTags.CombatProtection, ContainsAny(text, "prevent all combat damage", "prevent all damage", "phase out", "indestructible until end of turn"));
        AddTag(tags, DeckTags.CombatPayoff, ContainsCombatPayoffText(text));
        AddTag(tags, DeckTags.Evasion, ContainsAny(text, "flying", "trample", "menace", "can't be blocked", "unblockable"));
        AddTag(tags, DeckTags.Finishers, ContainsFinisherText(text));
        AddTag(tags, DeckTags.SacrificeFodder, ContainsAny(text, "create") && ContainsAny(text, "token"));
        AddTag(tags, DeckTags.Engines, ContainsAny(text, "whenever", "at the beginning") && ContainsAny(text, "draw", "create", "return", "lose 1 life"));
        AddTag(tags, DeckTags.ComboEnabler, ContainsAny(text, "untap", "copy", "activate only once", "as though it had flash") && ContainsAny(text, "add", "permanent", "ability", "spell"));

        if (snapshot.ProducedMana.Count > 1 || ContainsAny(text, "mana of any color", "any color"))
        {
            AddTag(tags, DeckTags.ManaFixing, true);
        }

        return tags;
    }

    /// <summary>
    /// Creates a role assignment.
    /// </summary>
    private static CardRoleAssignment Assignment(
        string primaryRole,
        List<string> tags,
        double confidence,
        IReadOnlyList<string>? functionalRoles = null)
    {
        CardRoleAssignment assignment = new()
        {
            PrimaryRole = primaryRole,
            Tags = tags,
            Confidence = confidence
        };
        AddFunctionalRole(assignment.FunctionalRoles, primaryRole);
        if (functionalRoles is not null)
        {
            AddFunctionalRoles(assignment.FunctionalRoles, functionalRoles);
        }

        return assignment;
    }

    /// <summary>
    /// Adds a tag when the condition matches.
    /// </summary>
    private static void AddTag(List<string> tags, string tag, bool condition)
    {
        if (!condition)
        {
            return;
        }

        foreach (string value in tags)
        {
            if (value.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        tags.Add(tag);
    }

    /// <summary>
    /// Adds a functional role when it is not already present.
    /// </summary>
    private static void AddFunctionalRole(List<string> roles, string role)
    {
        foreach (string value in roles)
        {
            if (value.Equals(role, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        roles.Add(role);
    }

    /// <summary>
    /// Adds functional roles from a precomputed set.
    /// </summary>
    private static void AddFunctionalRoles(List<string> roles, IReadOnlyList<string> additionalRoles)
    {
        foreach (string role in additionalRoles)
        {
            AddFunctionalRole(roles, role);
        }
    }

    /// <summary>
    /// Selects the highest-priority canonical Tagger rule.
    /// </summary>
    private static bool IsBetterTaggerRule(DeckTaggerRule candidate, DeckTaggerRule best)
    {
        if (candidate.Priority != best.Priority)
        {
            return candidate.Priority > best.Priority;
        }

        return RolePriority(candidate.Role) < RolePriority(best.Role);
    }

    /// <summary>
    /// Adds secondary tags backed by saved Scryfall Tagger oracle-card annotations.
    /// </summary>
    private static void AddCanonicalTaggerTags(List<string> tags, IReadOnlyList<string> taggerOracleTags)
    {
        foreach (string taggerTag in taggerOracleTags)
        {
            if (DeckTaggerTaxonomy.TryGetRule(taggerTag, out DeckTaggerRule? rule))
            {
                AddTag(tags, rule.SecondaryTag, condition: true);
                if (rule.SecondaryTag.Equals(DeckTags.Food, StringComparison.OrdinalIgnoreCase))
                {
                    AddTag(tags, DeckTags.Tokens, condition: true);
                    AddTag(tags, DeckTags.Lifegain, condition: true);
                }

                if (rule.SecondaryTag.Equals(DeckTags.ArtifactTokens, StringComparison.OrdinalIgnoreCase))
                {
                    AddTag(tags, DeckTags.Tokens, condition: true);
                }
            }
        }
    }

    /// <summary>
    /// Uses canonical Tagger annotations as the strongest functional signal after fixed deck-state roles.
    /// </summary>
    private static CardRoleAssignment? TryClassifyFromTaggerTags(
        IReadOnlyList<string> taggerOracleTags,
        List<string> tags)
    {
        DeckTaggerRule? best = null;
        foreach (string tag in taggerOracleTags)
        {
            if (!DeckTaggerTaxonomy.TryGetRule(tag, out DeckTaggerRule rule))
            {
                continue;
            }

            if (best is null || IsBetterTaggerRule(rule, best))
            {
                best = rule;
            }
        }

        return best is null ? null : Assignment(best.Role, tags, 0.9);
    }

    /// <summary>
    /// Gets the stable primary-role order for tie-breaking canonical Tagger rules.
    /// </summary>
    private static int RolePriority(string role)
    {
        for (int index = 0; index < DeckRoles.Primary.Count; index++)
        {
            if (DeckRoles.Primary[index].Equals(role, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Gets card categories safely.
    /// </summary>
    private static IEnumerable<string> Categories(DeckCard card)
    {
        return card.Categories ?? [];
    }

    /// <summary>
    /// Checks whether the primary category describes a land slot.
    /// </summary>
    private static bool HasPrimaryLandCategory(DeckCard card)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        return primaryCategory.Equals("Land", StringComparison.OrdinalIgnoreCase)
            || primaryCategory.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the primary type line for multi-face cards.
    /// </summary>
    private static string PrimaryTypeLine(string typeLine)
    {
        string[] faces = TypeLineFaces(typeLine);
        return faces.FirstOrDefault() ?? typeLine;
    }

    /// <summary>
    /// Checks whether a multi-face card has a land face behind a nonland primary face.
    /// </summary>
    private static bool HasNonPrimaryLandFace(string typeLine)
    {
        string[] faces = TypeLineFaces(typeLine);
        if (faces.Length <= 1 || ContainsAny(faces[0], "land"))
        {
            return false;
        }

        for (int index = 1; index < faces.Length; index++)
        {
            if (ContainsAny(faces[index], "land"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Classifies additive functional roles without changing the primary-role priority order.
    /// </summary>
    private static List<string> ClassifyFunctionalRoles(
        string categoryText,
        string oracleText,
        CardSnapshot snapshot,
        bool hasNonPrimaryLandFace)
    {
        List<string> roles = [];
        if (ContainsAny(categoryText, DeckRoles.Ramp)
            || (snapshot.ProducedMana.Count > 0 && !hasNonPrimaryLandFace)
            || ContainsRampText(oracleText, hasNonPrimaryLandFace))
        {
            AddFunctionalRole(roles, DeckRoles.Ramp);
        }

        if (ContainsAny(categoryText, DeckRoles.Draw)
            || ContainsDrawText(oracleText))
        {
            AddFunctionalRole(roles, DeckRoles.Draw);
        }

        if (ContainsAny(categoryText, DeckRoles.Tutors)
            || ContainsAny(oracleText, "search your library", "searches your library"))
        {
            AddFunctionalRole(roles, DeckRoles.Tutors);
        }

        return roles;
    }

    /// <summary>
    /// Splits a multi-face type line.
    /// </summary>
    private static string[] TypeLineFaces(string typeLine)
    {
        return typeLine.Split(
            ["//"],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );
    }

    /// <summary>
    /// Allows board-wipe categories to classify sparse cards without overriding source-backed rules text.
    /// </summary>
    private static bool AllowsBoardWipeCategoryFallback(
        CardSnapshot snapshot,
        string oracleText,
        IReadOnlyList<string> taggerOracleTags)
    {
        return string.IsNullOrWhiteSpace(oracleText)
            && snapshot.ProducedMana.Count == 0
            && taggerOracleTags.Count == 0;
    }

    /// <summary>
    /// Checks whether oracle text looks like ramp.
    /// </summary>
    private static bool ContainsRampText(string oracleText, bool hasNonPrimaryLandFace)
    {
        if (ContainsAny(oracleText, "treasure token", "search your library for a basic land", "search your library for a land"))
        {
            return true;
        }

        if (ContainsAny(oracleText, "search your library")
            && ContainsAny(oracleText, "battlefield")
            && ContainsAny(oracleText, "land card", "basic land", "forest card", "plains card", "island card", "swamp card", "mountain card"))
        {
            return true;
        }

        if (ContainsAny(oracleText, "cost {1} less", "costs {1} less", "cost one less", "costs one less", "cost less to cast"))
        {
            return true;
        }

        return !hasNonPrimaryLandFace && ContainsAny(oracleText, "add {", "add one mana", "add two mana");
    }

    /// <summary>
    /// Checks whether rules text creates card advantage or card replacement that should count as draw density.
    /// </summary>
    private static bool ContainsDrawText(string oracleText)
    {
        if (ContainsAny(oracleText, "draw a card", "draw two", "draw three", "draw cards", "draw that many"))
        {
            return true;
        }

        if (ContainsAny(oracleText, "discard")
            && ContainsAny(oracleText, "draw a card", "draw two", "draw cards"))
        {
            return true;
        }

        if (ContainsAny(oracleText, "sacrifice an artifact", "sacrifice a creature", "sacrifice another")
            && ContainsAny(oracleText, "draw a card", "draw two", "draw cards"))
        {
            return true;
        }

        return ContainsAny(oracleText, "exile the top card", "exile the top two", "exile cards from the top")
            && ContainsAny(oracleText, "you may play", "you may cast")
            && ContainsAny(
                oracleText,
                "this turn",
                "until end of turn",
                "until your next turn",
                "until the end of your next turn");
    }

    /// <summary>
    /// Checks whether rules text describes a broad destructive or reset effect.
    /// </summary>
    private static bool ContainsBoardWipeText(string oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText))
        {
            return false;
        }

        if (ContainsAny(
                oracleText,
                "destroy all",
                "exile all",
                "destroy each",
                "exile each",
                "return all creatures",
                "return each creature",
                "return all nonland",
                "return each nonland"))
        {
            return true;
        }

        if (ContainsAny(
                oracleText,
                "all creatures get -",
                "each creature gets -",
                "all nonland creatures get -",
                "each nonland creature gets -"))
        {
            return true;
        }

        if (BoardDamageRegex().IsMatch(oracleText))
        {
            return true;
        }

        return ContainsAny(oracleText, "all creatures", "each creature", "each nonland")
            && ContainsAny(oracleText, "destroy", "exile", "damage", "sacrifice", "-x/-x");
    }

    /// <summary>
    /// Checks whether rules text can protect another important permanent rather than only itself.
    /// </summary>
    private static bool ContainsCommanderProtectionText(string oracleText)
    {
        bool protectionKeyword = ContainsAny(
            oracleText,
            "hexproof",
            "shroud",
            "indestructible",
            "protection from",
            "prevent all damage",
            "phase out");
        if (!protectionKeyword)
        {
            return false;
        }

        return ContainsAny(
            oracleText,
            "equipped creature",
            "enchanted creature",
            "target creature",
            "target permanent",
            "creature you control",
            "permanent you control",
            "creatures you control",
            "permanents you control",
            "you control gain",
            "you control have",
            "prevent all damage");
    }

    /// <summary>
    /// Checks whether text describes a game-closing effect rather than incidental life loss.
    /// </summary>
    private static bool ContainsFinisherText(string text)
    {
        if (ContainsAny(
            text,
            "win the game",
            "loses half their life",
            "repeat this process",
            "repeat the following process",
            "creatures you control get +",
            "extra combat",
            "damage to each opponent equal"))
        {
            return true;
        }

        return ContainsCombatPayoffText(text) || FinisherLifeLossRegex().IsMatch(text);
    }

    /// <summary>
    /// Checks whether text provides a deterministic team-combat payoff or closer.
    /// </summary>
    private static bool ContainsCombatPayoffText(string text)
    {
        return ContainsAny(
            text,
            "attacking creatures you control have double strike",
            "other creatures you control have melee",
            "creatures you control have melee",
            "battle cry",
            "creatures you control get +",
            "creatures you control gain trample",
            "creatures you control have trample",
            "creatures you control gain flying",
            "creatures you control have flying",
            "creatures you control gain menace",
            "creatures you control have menace",
            "creatures you control gain haste",
            "creatures you control have haste",
            "creatures you control can't be blocked",
            "creatures can't block creatures you control",
            "creatures without flying can't block",
            "whenever a creature you control attacks, it gets +",
            "as long as you control your commander, creatures you control get +");
    }

    /// <summary>
    /// Checks whether rules text creates or references Food tokens.
    /// </summary>
    private static bool ContainsFoodText(string text)
    {
        return ContainsAny(text, "food token", "food tokens", "foods you control", "sacrifice a food", "sacrificed a food");
    }

    /// <summary>
    /// Checks whether rules text creates artifact tokens that matter beyond creature count.
    /// </summary>
    private static bool ContainsArtifactTokenText(string text)
    {
        return ContainsAny(
            text,
            "food token",
            "food tokens",
            "treasure token",
            "treasure tokens",
            "clue token",
            "clue tokens",
            "blood token",
            "blood tokens",
            "map token",
            "map tokens",
            "artifact token",
            "artifact tokens");
    }

    /// <summary>
    /// Checks whether rules text uses cards from graveyards as recursion resources.
    /// </summary>
    private static bool ContainsGraveyardRecursionText(string text)
    {
        return ContainsAny(
            text,
            "from your graveyard to the battlefield",
            "from a graveyard to the battlefield",
            "return target creature card",
            "return target permanent card from your graveyard",
            "return target card from your graveyard",
            "return a creature card from your graveyard",
            "you may cast this card from your graveyard",
            "you may play lands and cast spells from your graveyard",
            "escape",
            "unearth");
    }

    /// <summary>
    /// Checks whether rules text answers graveyards rather than using your own graveyard.
    /// </summary>
    private static bool ContainsGraveyardHateText(string text)
    {
        return ContainsAny(
            text,
            "exile target card from a graveyard",
            "exile target player's graveyard",
            "exile all cards from target player's graveyard",
            "exile all graveyards",
            "exile each graveyard",
            "cards in graveyards can't",
            "players can't cast spells from graveyards",
            "graveyard can't",
            "graveyards can't",
            "would be put into a graveyard, exile it instead");
    }

    /// <summary>
    /// Checks whether rules text answers artifacts or enchantments.
    /// </summary>
    private static bool ContainsArtifactEnchantmentHateText(string text)
    {
        return ContainsAny(
            text,
            "destroy target artifact",
            "destroy target enchantment",
            "exile target artifact",
            "exile target enchantment",
            "destroy all artifacts",
            "destroy all enchantments",
            "artifacts and enchantments",
            "artifact or enchantment",
            "target creature or enchantment",
            "sacrifices an enchantment",
            "sacrifices an artifact");
    }

    /// <summary>
    /// Checks whether text contains any needles.
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
    /// Matches X-life finisher wording that is easier to express as a regex than as fixed phrases.
    /// </summary>
    [GeneratedRegex(
        @"each opponent loses\s+x\s+life|each opponent loses life equal",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FinisherLifeLossRegex();

    /// <summary>
    /// Matches broad creature-damage sweepers such as Blasphemous Act without matching pump text.
    /// </summary>
    [GeneratedRegex(
        @"deals\s+(?:x|\d+|that much)\s+damage\s+to\s+(?:each|all)\s+creatures?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BoardDamageRegex();

    /// <summary>
    /// Splits locally stored annotation values using the same separators as facet snapshots.
    /// </summary>
    private static List<string> AnnotationValues(
        IReadOnlyDictionary<string, string> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([',', ';', '|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets snapshot text with legacy metadata fallback.
    /// </summary>
    private static string Text(
        string? snapshotValue,
        IReadOnlyDictionary<string, string> metadata,
        string metadataKey
    )
    {
        if (!string.IsNullOrWhiteSpace(snapshotValue))
        {
            return snapshotValue;
        }

        return metadata.TryGetValue(metadataKey, out string? value) ? value : "";
    }
}
