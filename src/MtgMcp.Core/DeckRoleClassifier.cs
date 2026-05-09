namespace MtgMcp.Core;

public static class DeckRoleClassifier
{
    public static CardRoleAssignment Classify(DeckCard card)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        string categoryText = string.Join(' ', Categories(card).Append(card.PrimaryCategory ?? ""));
        string typeLine = Text(snapshot.TypeLine, card.Metadata, "typeLine");
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

        if (ContainsAny(categoryText, DeckRoles.Lands) || ContainsAny(typeLine, "land"))
        {
            return Assignment(DeckRoles.Lands, tags, 0.93);
        }

        if (ContainsAny(categoryText, DeckRoles.Ramp)
            || snapshot.ProducedMana.Count > 0
            || ContainsAny(oracleText, "add {", "add one mana", "add two mana", "treasure token", "search your library for a basic land", "search your library for a land"))
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
            || ContainsAny(oracleText, "destroy target", "exile target", "counter target", "target creature gets", "fight target", "deals damage to target"))
        {
            return Assignment(DeckRoles.Interaction, tags, 0.78);
        }

        if (ContainsAny(categoryText, DeckRoles.Protection)
            || ContainsAny(oracleText, "hexproof", "indestructible", "protection from", "prevent all damage", "phase out"))
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
            _ => ""
        };

        return string.IsNullOrWhiteSpace(roleQuery)
            ? legal.Trim()
            : $"{roleQuery}{legal}{price}".Trim();
    }

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

    private static CardRoleAssignment Assignment(string primaryRole, List<string> tags, double confidence)
    {
        return new CardRoleAssignment
        {
            PrimaryRole = primaryRole,
            Tags = tags,
            Confidence = confidence
        };
    }

    private static void AddTag(List<string> tags, string tag, bool condition)
    {
        if (condition && !tags.Any(value => value.Equals(tag, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(tag);
        }
    }

    private static bool HasCategory(DeckCard card, string category)
    {
        return Categories(card).Any(value => value.Equals(category, StringComparison.OrdinalIgnoreCase))
            || string.Equals(card.PrimaryCategory, category, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Categories(DeckCard card)
    {
        return card.Categories ?? [];
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string Text(string? snapshotValue, IReadOnlyDictionary<string, string> metadata, string metadataKey)
    {
        if (!string.IsNullOrWhiteSpace(snapshotValue))
        {
            return snapshotValue;
        }

        return metadata.TryGetValue(metadataKey, out string? value) ? value : "";
    }
}
