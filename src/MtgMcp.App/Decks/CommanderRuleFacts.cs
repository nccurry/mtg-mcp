using MtgMcp.Core.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Names a deck entry that a rule fact points at.
/// </summary>
internal sealed record RuleEntryRef(Guid EntryId, string Name);

/// <summary>
/// Reports the deck size rule: the number of cards in the commander and main zones against the required number.
/// </summary>
internal sealed record DeckSizeFact(bool Passed, int Count, int Expected);

/// <summary>
/// Reports one entry of the commander zone and whether it can be a commander.
/// </summary>
internal sealed record CommanderItem(
    Guid EntryId,
    string Name,
    bool Eligible,
    string? Via,
    string? Reason,
    string? OracleText);

/// <summary>
/// Reports the commanders and whether each is eligible. Eligibility follows the Comprehensive Rules, rule 903.3.
/// </summary>
internal sealed record CommandersFact(bool Passed, int Count, IReadOnlyList<CommanderItem> Items, string? Reason);

/// <summary>
/// Names a main-zone entry whose color identity is outside the commanders' identity.
/// </summary>
internal sealed record OffIdentityEntry(Guid EntryId, string Name, IReadOnlyList<string> ColorIdentity);

/// <summary>
/// Reports the color identity rule: the commanders' combined identity and the entries outside it.
/// </summary>
internal sealed record ColorIdentityFact(
    bool? Passed,
    IReadOnlyList<string> Identity,
    IReadOnlyList<OffIdentityEntry> OffIdentity);

/// <summary>
/// Names a card that has more copies than a deck may hold.
/// </summary>
internal sealed record SingletonBreak(string Name, int Count, int Limit, IReadOnlyList<Guid> EntryIds);

/// <summary>
/// Reports the singleton rule: cards with more copies than allowed.
/// </summary>
internal sealed record SingletonFact(bool Passed, IReadOnlyList<SingletonBreak> Breaks);

/// <summary>
/// Names an entry whose Scryfall legality for Commander is banned or not legal.
/// </summary>
internal sealed record LegalityIssue(Guid EntryId, string Name, string Status);

/// <summary>
/// Reports the Commander legality of every entry from Scryfall's legality data.
/// </summary>
internal sealed record LegalityFact(bool Passed, IReadOnlyList<LegalityIssue> Issues);

/// <summary>
/// Reports how many Game Changers the deck holds. It has no passed flag, because the number allowed depends on a
/// bracket that the caller chooses. Status is unknown when some entry's data lacks the Game Changer field.
/// </summary>
internal sealed record GameChangersFact(
    string Status,
    int Count,
    IReadOnlyList<RuleEntryRef> Entries,
    IReadOnlyList<RuleEntryRef>? UnknownEntries);

/// <summary>
/// Describes one partner-like ability read from a card's keywords and text.
/// </summary>
internal sealed record PairingAbility(string Kind, string? Detail);

/// <summary>
/// Describes one commander of a pair with the abilities read from it.
/// </summary>
internal sealed record PairingCard(Guid EntryId, string Name, IReadOnlyList<PairingAbility> Abilities);

/// <summary>
/// Reports whether two commanders may be paired. Status is passed, failed, or unknown when a pairing ability is one
/// the rules table does not list.
/// </summary>
internal sealed record PairingFact(
    string Status,
    bool? Passed,
    IReadOnlyList<PairingCard> Cards,
    string Reason);

/// <summary>
/// Reports one companion: its restriction text, the identity and legality checks, and the restriction check.
/// </summary>
internal sealed record CompanionFact(
    Guid EntryId,
    string Name,
    string? Restriction,
    string Status,
    bool? Passed,
    bool? InCommanderIdentity,
    bool LegalInCommander,
    string RestrictionStatus,
    string RestrictionReason,
    IReadOnlyList<RuleEntryRef> RestrictionEntries);

/// <summary>
/// Names an entry the facts could not read because it has no Scryfall card.
/// </summary>
internal sealed record UnresolvedRuleEntry(Guid EntryId, string Name, string Reason);

/// <summary>
/// Holds every Commander rule fact of one deck. No fact names a bracket, a power level, or an overall verdict.
/// </summary>
internal sealed record CommanderRuleFacts(
    string Rules,
    DeckSizeFact DeckSize,
    CommandersFact Commanders,
    ColorIdentityFact ColorIdentity,
    SingletonFact Singleton,
    LegalityFact Legality,
    GameChangersFact GameChangers,
    PairingFact? Pairing,
    IReadOnlyList<CompanionFact> Companions,
    IReadOnlyList<UnresolvedRuleEntry> Unresolved,
    IReadOnlyList<string> Limits);

/// <summary>
/// Pairs a deck entry with its Scryfall card, or with the reason there is none.
/// </summary>
internal sealed record RuleEntry(DeckEntry Entry, ScryfallCard? Card, string? UnresolvedReason);

/// <summary>
/// Works out the Commander rule facts of a deck from its entries and their Scryfall cards. It does no input or
/// output and judges nothing beyond each fact's stated rule.
/// </summary>
internal static class CommanderRules
{
    /// <summary>
    /// Gives the number of cards a Commander deck must hold.
    /// </summary>
    internal const int DeckSize = 100;

    /// <summary>
    /// Lists the five colors in their standard order.
    /// </summary>
    private static readonly string[] ColorOrder = ["W", "U", "B", "R", "G"];

    /// <summary>
    /// Evaluates every Commander rule fact for the entries of one deck.
    /// </summary>
    internal static CommanderRuleFacts Evaluate(IReadOnlyList<RuleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<RuleEntry> commanders = InZone(entries, "commander");
        List<RuleEntry> main = InZone(entries, "main");
        List<RuleEntry> companions = InZone(entries, "companion");
        List<RuleEntry> starting = [.. commanders, .. main];
        CommandersFact commanderFact = EvaluateCommanders(commanders);
        ColorIdentityFact identity = EvaluateIdentity(commanders, main);
        List<string> limits = ["Facts cover entries whose Scryfall card was found; the others are in unresolved."];
        return new CommanderRuleFacts(
            "commander",
            new DeckSizeFact(starting.Sum(entry => entry.Entry.Quantity) == DeckSize,
                starting.Sum(entry => entry.Entry.Quantity), DeckSize),
            commanderFact,
            identity,
            EvaluateSingleton(starting),
            EvaluateLegality(starting),
            EvaluateGameChangers(starting),
            EvaluatePairing(commanders),
            [.. companions.Where(entry => entry.Card is not null).Select(entry => EvaluateCompanion(entry, identity,
                starting))],
            [.. entries
                .Where(entry => entry.Card is null && IsRuleZone(entry.Entry.Zone))
                .Select(entry => new UnresolvedRuleEntry(entry.Entry.EntryId, entry.Entry.CardName,
                    entry.UnresolvedReason ?? "not-in-card-data"))],
            limits);
    }

    /// <summary>
    /// Reports whether a zone is one that the facts read.
    /// </summary>
    private static bool IsRuleZone(string zone)
    {
        return zone.Equals("commander", StringComparison.OrdinalIgnoreCase) ||
            zone.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            zone.Equals("companion", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lists the entries in one zone, matching the zone name without regard to letter case.
    /// </summary>
    private static List<RuleEntry> InZone(IReadOnlyList<RuleEntry> entries, string zone)
    {
        return [.. entries.Where(entry => entry.Entry.Zone.Equals(zone, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Checks each commander-zone entry against the commander eligibility rule.
    /// </summary>
    private static CommandersFact EvaluateCommanders(List<RuleEntry> commanders)
    {
        bool hasChooseBackground = commanders.Any(entry => entry.Card is not null &&
            CommanderCardText.HasKeyword(entry.Card, "Choose a Background"));
        List<CommanderItem> items = [];
        foreach (RuleEntry entry in commanders)
        {
            if (entry.Card is null)
            {
                items.Add(new CommanderItem(
                    entry.Entry.EntryId,
                    entry.Entry.CardName,
                    false,
                    null,
                    "No Scryfall card was found for this entry.",
                    null));
                continue;
            }

            string? via = EligibleVia(entry.Card, hasChooseBackground);
            items.Add(new CommanderItem(
                entry.Entry.EntryId,
                entry.Card.Name,
                via is not null,
                via,
                via is null
                    ? "Not a legendary creature, Vehicle, or Spacecraft with power and toughness, and its text " +
                        "does not say it can be your commander."
                    : null,
                ScryfallCardFaces.FrontText(entry.Card)));
        }

        int count = commanders.Sum(entry => entry.Entry.Quantity);
        string? reason = count switch
        {
            0 => "The deck has no commander.",
            > 2 => "A deck has at most two commanders.",
            _ => null,
        };
        return new CommandersFact(reason is null && items.All(item => item.Eligible), count, items, reason);
    }

    /// <summary>
    /// Says why a card may be a commander, or null when it may not.
    /// </summary>
    private static string? EligibleVia(ScryfallCard card, bool pairedWithChooseBackground)
    {
        IReadOnlyList<string> types = ScryfallCardFaces.FrontTypes(card);
        IReadOnlyList<string> subtypes = ScryfallCardFaces.FrontSubtypes(card);
        bool legendary = types.Contains("Legendary", StringComparer.Ordinal);
        if (legendary && types.Contains("Creature", StringComparer.Ordinal))
        {
            return "legendary-creature";
        }

        if (legendary && subtypes.Contains("Vehicle", StringComparer.Ordinal))
        {
            return "legendary-vehicle";
        }

        if (legendary && subtypes.Contains("Spacecraft", StringComparer.Ordinal) && HasPowerAndToughness(card))
        {
            return "legendary-spacecraft";
        }

        if (CommanderCardText.SaysCanBeCommander(card))
        {
            return "can-be-your-commander";
        }

        return pairedWithChooseBackground && CommanderCardText.IsBackground(card) ? "background" : null;
    }

    /// <summary>
    /// Reports whether the front face has a power and a toughness.
    /// </summary>
    private static bool HasPowerAndToughness(ScryfallCard card)
    {
        ScryfallCardFace? face = card.Faces.Count > 0 ? card.Faces[0] : null;
        string? power = face?.Power ?? card.Power;
        string? toughness = face?.Toughness ?? card.Toughness;
        return !string.IsNullOrEmpty(power) && !string.IsNullOrEmpty(toughness);
    }

    /// <summary>
    /// Combines the commanders' color identities and finds the main-zone entries outside it.
    /// </summary>
    private static ColorIdentityFact EvaluateIdentity(List<RuleEntry> commanders, List<RuleEntry> main)
    {
        HashSet<string> identity = [];
        foreach (RuleEntry entry in commanders.Where(entry => entry.Card is not null))
        {
            identity.UnionWith(entry.Card!.ColorIdentity);
        }

        if (commanders.Any(entry => entry.Card is null))
        {
            // A commander with no card data has an unknown identity, so no entry can be called outside it.
            return new ColorIdentityFact(null, SortColors(identity), []);
        }

        List<OffIdentityEntry> off = [];
        foreach (RuleEntry entry in main.Where(entry => entry.Card is not null))
        {
            if (!entry.Card!.ColorIdentity.All(identity.Contains))
            {
                off.Add(new OffIdentityEntry(entry.Entry.EntryId, entry.Card.Name, entry.Card.ColorIdentity));
            }
        }

        return new ColorIdentityFact(off.Count == 0, SortColors(identity), off);
    }

    /// <summary>
    /// Sorts colors in the order white, blue, black, red, green.
    /// </summary>
    private static List<string> SortColors(IEnumerable<string> colors)
    {
        return [.. colors.OrderBy(color => Array.IndexOf(ColorOrder, color)).ThenBy(color => color,
            StringComparer.Ordinal)];
    }

    /// <summary>
    /// Finds cards with more copies than a deck may hold.
    /// </summary>
    private static SingletonFact EvaluateSingleton(List<RuleEntry> starting)
    {
        List<SingletonBreak> breaks = [];
        foreach (IGrouping<string, RuleEntry> group in starting
            .Where(entry => entry.Card is not null)
            .GroupBy(entry => ScryfallCardSets.NormalizeName(entry.Card!.Name), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            ScryfallCard card = group.First().Card!;
            int? limit = CommanderCardText.CopyLimit(card);
            int count = group.Sum(entry => entry.Entry.Quantity);
            if (limit is int allowed && count > allowed)
            {
                breaks.Add(new SingletonBreak(card.Name, count, allowed,
                    [.. group.Select(entry => entry.Entry.EntryId)]));
            }
        }

        return new SingletonFact(breaks.Count == 0, breaks);
    }

    /// <summary>
    /// Reports whether Scryfall lists a card as banned or not legal in Commander, and gives that status. A card with
    /// no Commander entry, or any other status (legal, restricted), is not blocked. Both the legality fact and the
    /// companion check use this one rule.
    /// </summary>
    private static bool BlocksCommander(ScryfallCard card, out string? status)
    {
        return card.Legalities.TryGetValue("commander", out status) && status is "banned" or "not_legal";
    }

    /// <summary>
    /// Lists entries whose Commander legality is banned or not legal.
    /// </summary>
    private static LegalityFact EvaluateLegality(List<RuleEntry> starting)
    {
        List<LegalityIssue> issues = [];
        foreach (RuleEntry entry in starting.Where(entry => entry.Card is not null))
        {
            if (BlocksCommander(entry.Card!, out string? status))
            {
                issues.Add(new LegalityIssue(entry.Entry.EntryId, entry.Card!.Name, status!));
            }
        }

        return new LegalityFact(issues.Count == 0, issues);
    }

    /// <summary>
    /// Counts the entries Scryfall marks as Game Changers, and notes entries whose data lacks the field.
    /// </summary>
    private static GameChangersFact EvaluateGameChangers(List<RuleEntry> starting)
    {
        List<RuleEntryRef> changers = [];
        List<RuleEntryRef> unknown = [];
        int count = 0;
        foreach (RuleEntry entry in starting.Where(entry => entry.Card is not null))
        {
            RuleEntryRef reference = new(entry.Entry.EntryId, entry.Card!.Name);
            if (entry.Card.GameChanger is null)
            {
                unknown.Add(reference);
            }
            else if (entry.Card.GameChanger == true)
            {
                changers.Add(reference);
                count += entry.Entry.Quantity;
            }
        }

        return new GameChangersFact(unknown.Count == 0 ? "known" : "unknown", count, changers, unknown.Count == 0
            ? null : unknown);
    }

    /// <summary>
    /// Checks whether the two commanders may be paired, or returns null when the deck does not have exactly two.
    /// </summary>
    private static PairingFact? EvaluatePairing(List<RuleEntry> commanders)
    {
        int count = commanders.Sum(entry => entry.Entry.Quantity);
        if (count != 2)
        {
            return null;
        }

        if (commanders.Count == 1)
        {
            return new PairingFact("failed", false, [], "The same card cannot be both commanders.");
        }

        if (commanders.Any(entry => entry.Card is null))
        {
            return new PairingFact(
                "unknown",
                null,
                [],
                "A commander has no Scryfall card, so its abilities are unknown.");
        }

        return CommanderPairing.Check(commanders[0], commanders[1]);
    }

    /// <summary>
    /// Checks a companion's identity, legality, and restriction.
    /// </summary>
    private static CompanionFact EvaluateCompanion(RuleEntry entry, ColorIdentityFact identity,
        List<RuleEntry> starting)
    {
        ScryfallCard card = entry.Card!;
        bool? inIdentity = identity.Passed is null ? null : card.ColorIdentity.All(identity.Identity.Contains);
        bool legal = !BlocksCommander(card, out _);
        (string restrictionStatus, string reason, List<RuleEntryRef> entries) = CompanionRestrictions.Check(card,
            starting);
        bool failed = inIdentity == false || !legal || restrictionStatus == "failed";
        string overall = failed ? "failed"
            : restrictionStatus == "not-checked" || inIdentity is null ? "not-checked"
            : "passed";
        return new CompanionFact(
            entry.Entry.EntryId,
            card.Name,
            CommanderCardText.CompanionRestriction(card),
            overall,
            overall switch
            {
                "failed" => false,
                "passed" => true,
                _ => null,
            },
            inIdentity,
            legal,
            restrictionStatus,
            reason,
            entries);
    }
}
