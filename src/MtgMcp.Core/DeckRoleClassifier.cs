namespace MtgMcp.Core;

/// <summary>
/// Classifies cards into deck roles and tags.
/// </summary>
public static class DeckRoleClassifier
{
    /// <summary>
    /// Classifies the card.
    /// </summary>
    public static CardRoleAssignment Classify(DeckCard card)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        string categoryText = string.Join(' ', Categories(card).Append(card.PrimaryCategory ?? ""));
        string typeLine = Text(snapshot.TypeLine, card.Metadata, "typeLine");
        string primaryTypeLine = PrimaryTypeLine(typeLine);
        bool hasNonPrimaryLandFace = HasNonPrimaryLandFace(typeLine);
        string oracleText = Text(snapshot.OracleText, card.Metadata, "oracleText");
        string combined = $"{card.Name} {categoryText} {typeLine} {oracleText}";

        List<string> tags = ClassifyTags(card, combined);

        if (HasCategory(card, DeckDefaults.Maybeboard))
        {
            return Assignment(DeckRoles.Maybeboard, tags, 0.95);
        }

        if (HasCategory(card, DeckRoles.Commander) || HasCategory(card, "Commander"))
        {
            return Assignment(DeckRoles.Commander, tags, 0.95);
        }

        if (ContainsAny(categoryText, DeckRoles.Lands) || ContainsAny(primaryTypeLine, "land"))
        {
            return Assignment(DeckRoles.Lands, tags, 0.93);
        }

        if (ContainsAny(categoryText, DeckRoles.Ramp)
            || (snapshot.ProducedMana.Count > 0 && !hasNonPrimaryLandFace)
            || ContainsRampText(oracleText, hasNonPrimaryLandFace))
        {
            AddTag(tags, DeckTags.ManaFixing, snapshot.ProducedMana.Count > 1 || ContainsAny(oracleText, "mana of any color", "any color"));
            return Assignment(DeckRoles.Ramp, tags, 0.85);
        }

        if (ContainsAny(categoryText, DeckRoles.Draw)
            || ContainsAny(oracleText, "draw a card", "draw two", "draw three", "draw cards", "draw that many"))
        {
            return Assignment(DeckRoles.Draw, tags, 0.82);
        }

        if (ContainsAny(categoryText, DeckRoles.Tutors)
            || ContainsAny(oracleText, "search your library", "searches your library"))
        {
            return Assignment(DeckRoles.Tutors, tags, 0.84);
        }

        if (ContainsAny(categoryText, DeckRoles.BoardWipes, "wipe")
            || ContainsAny(oracleText, "destroy all", "exile all", "all creatures", "each creature", "each nonland"))
        {
            return Assignment(DeckRoles.BoardWipes, tags, 0.82);
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
            return Assignment(DeckRoles.Interaction, tags, 0.78);
        }

        if (ContainsAny(categoryText, DeckRoles.Protection)
            || ContainsAny(oracleText, "hexproof", "shroud", "indestructible", "protection from", "prevent all damage", "phase out"))
        {
            return Assignment(DeckRoles.Protection, tags, 0.78);
        }

        if (ContainsAny(categoryText, DeckRoles.Recursion)
            || ContainsAny(oracleText, "return target", "from your graveyard", "from a graveyard to the battlefield", "return a creature card"))
        {
            return Assignment(DeckRoles.Recursion, tags, 0.76);
        }

        if (ContainsAny(categoryText, DeckRoles.Wincons, "finisher")
            || ContainsAny(oracleText, "win the game", "each opponent loses", "loses half their life", "damage to each opponent"))
        {
            return Assignment(DeckRoles.Wincons, tags, 0.72);
        }

        if (ContainsAny(categoryText, DeckRoles.Payoffs, "payoff")
            || ContainsAny(oracleText, "whenever you discard", "whenever you draw", "whenever one or more", "whenever a creature dies"))
        {
            return Assignment(DeckRoles.Payoffs, tags, 0.68);
        }

        if (ContainsAny(categoryText, DeckRoles.Synergy)
            || ContainsAny(combined, "synergy", "engine", "combo"))
        {
            return Assignment(DeckRoles.Synergy, tags, 0.62);
        }

        return Assignment(DeckRoles.Utility, tags, tags.Count > 0 ? 0.55 : 0.35);
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

        if (Categories(card).Any(category => category.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(card.PrimaryCategory, target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a Scryfall search query for a role.
    /// </summary>
    public static string QueryForRole(string role, string? format, decimal? maxPrice = null)
    {
        string legal = string.IsNullOrWhiteSpace(format) ? "" : $" legal:{format}";
        string price = maxPrice.HasValue ? $" usd<={maxPrice.Value:0.##}" : "";
        string roleQuery = role.ToLowerInvariant() switch
        {
            "lands" => "t:land",
            "ramp" => "(o:add or o:treasure or o:\"search your library for a land\")",
            "draw" => "o:draw",
            "tutors" => "o:\"search your library\"",
            "interaction" => "(o:\"destroy target\" or o:\"exile target\" or o:\"counter target\")",
            "board wipes" => "(o:\"destroy all\" or o:\"exile all\" or o:\"all creatures\")",
            "protection" => "(o:hexproof or o:indestructible or o:\"phase out\")",
            "recursion" => "(o:graveyard o:return)",
            "wincons" => "(o:\"win the game\" or o:\"each opponent loses\")",
            "card selection" => "(o:scry or o:surveil or o:\"look at the top\" or o:\"reveal the top\")",
            _ => ""
        };

        return string.IsNullOrWhiteSpace(roleQuery)
            ? legal.Trim()
            : $"{roleQuery}{legal}{price}".Trim();
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
        AddTag(tags, DeckTags.Reanimation, ContainsAny(text, "from your graveyard to the battlefield", "return target creature card"));
        AddTag(tags, DeckTags.GraveyardHate, ContainsAny(text, "exile target card from a graveyard", "exile all graveyards", "graveyards"));
        AddTag(tags, DeckTags.Stax, ContainsAny(text, "can't cast", "can't attack", "doesn't untap", "skip", "players can't"));
        AddTag(tags, DeckTags.ComboPiece, ContainsAny(text, "combo", "untap") && ContainsAny(text, "add", "whenever", "copy"));
        AddTag(tags, DeckTags.CardSelection, ContainsAny(text, "scry", "surveil", "look at the top", "reveal the top"));
        AddTag(tags, DeckTags.Lifegain, ContainsAny(text, "gain life", "lifelink"));
        AddTag(tags, DeckTags.Drain, ContainsAny(text, "opponent loses") && ContainsAny(text, "you gain"));
        AddTag(tags, DeckTags.Voltron, ContainsAny(text, "equipment", "equip", "aura", "enchanted creature"));
        AddTag(tags, DeckTags.Blink, ContainsAny(text, "exile") && ContainsAny(text, "return it", "return that card", "under its owner's control"));
        AddTag(tags, DeckTags.Mill, ContainsAny(text, "mill"));
        AddTag(tags, DeckTags.Politics, ContainsAny(text, "goad", "vote", "monarch", "tempting offer"));

        if (snapshot.ProducedMana.Count > 1 || ContainsAny(text, "mana of any color", "any color"))
        {
            AddTag(tags, DeckTags.ManaFixing, true);
        }

        return tags;
    }

    /// <summary>
    /// Creates a role assignment.
    /// </summary>
    private static CardRoleAssignment Assignment(string primaryRole, List<string> tags, double confidence)
    {
        return new CardRoleAssignment
        {
            PrimaryRole = primaryRole,
            Tags = tags,
            Confidence = confidence
        };
    }

    /// <summary>
    /// Adds a tag when the condition matches.
    /// </summary>
    private static void AddTag(List<string> tags, string tag, bool condition)
    {
        if (condition && !tags.Any(value => value.Equals(tag, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(tag);
        }
    }

    /// <summary>
    /// Checks whether the card has a category.
    /// </summary>
    private static bool HasCategory(DeckCard card, string category)
    {
        return Categories(card).Any(value => value.Equals(category, StringComparison.OrdinalIgnoreCase))
            || string.Equals(card.PrimaryCategory, category, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets card categories safely.
    /// </summary>
    private static IEnumerable<string> Categories(DeckCard card)
    {
        return card.Categories ?? [];
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
    /// Checks whether oracle text looks like ramp.
    /// </summary>
    private static bool ContainsRampText(string oracleText, bool hasNonPrimaryLandFace)
    {
        if (ContainsAny(oracleText, "treasure token", "search your library for a basic land", "search your library for a land"))
        {
            return true;
        }

        return !hasNonPrimaryLandFace && ContainsAny(oracleText, "add {", "add one mana", "add two mana");
    }

    /// <summary>
    /// Checks whether text contains any needles.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
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
