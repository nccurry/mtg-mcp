namespace MtgMcp.Core;

/// <summary>
/// Contains local-meta pressure inference and aggregation internals.
/// </summary>
public sealed partial class DeckPlaygroupMetaScoringService
{
    /// <summary>
    /// Infers pressure categories from Playgroup deck metadata and optional imported deck cards.
    /// </summary>
    private static List<PlaygroupMetaPressureEvidence> InferDeckPressures(
        PlaygroupDeckSummary deck,
        DeckWorkspace? imported)
    {
        List<PlaygroupMetaPressureEvidence> pressures = [];
        string text = string.Join(' ', deck.Name, string.Join(' ', deck.CommanderNames));
        AddPressure(pressures, FastComboPressure, 0.75, "playgroup-summary", text, "combo", "turbo", "storm", "dork", "raggadragga");
        AddPressure(pressures, StackControlPressure, 0.7, "playgroup-summary", text, "control", "talion", "faerie", "counter", "permission");
        AddPressure(pressures, GoWideTokensPressure, 0.65, "playgroup-summary", text, "tokens", "saproling", "sap attack", "go-wide");
        AddPressure(pressures, GraveyardRecursionPressure, 0.65, "playgroup-summary", text, "graveyard", "reanimator", "dredge", "sac", "aristocrat");
        AddPressure(pressures, LifePressure, 0.55, "playgroup-summary", text, "slug", "burn", "norin", "ashling", "purphoros");
        if (deck.AverageWinsByRound is <= 6)
        {
            pressures.Add(new PlaygroupMetaPressureEvidence
            {
                Pressure = FastComboPressure,
                Score = 0.70,
                Source = "playgroup-stats",
                Evidence = [$"average winning round {deck.AverageWinsByRound:0.0} suggests early kill pressure"],
            });
        }

        if (imported is not null)
        {
            pressures.AddRange(InferImportedDeckPressures(imported));
        }

        return pressures
            .GroupBy(pressure => pressure.Pressure, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PlaygroupMetaPressureEvidence
            {
                Pressure = group.Key,
                Score = Math.Clamp(group.Max(item => item.Score), 0, 1),
                Source = string.Join(", ", group.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase)),
                Evidence = group.SelectMany(item => item.Evidence).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList(),
            })
            .OrderByDescending(pressure => pressure.Score)
            .ThenBy(pressure => pressure.Pressure, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Infers pressure from an imported Archidekt decklist.
    /// </summary>
    private static List<PlaygroupMetaPressureEvidence> InferImportedDeckPressures(DeckWorkspace imported)
    {
        List<DeckCard> cards = DeckServiceHelpers.IncludedCards(imported).ToList();
        int creatures = cards
            .Where(card => DeckAnalysisMetrics.ContainsAny(DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "", "Creature"))
            .Sum(card => Math.Max(1, card.Quantity));
        int ramp = CountCards(cards, role: DeckRoles.Ramp);
        int tutors = CountCards(cards, role: DeckRoles.Tutors);
        int interaction = CountCards(cards, role: DeckRoles.Interaction) + CountCards(cards, role: DeckRoles.BoardWipes);
        int stax = CountTaggedCards(cards, DeckTags.Stax);
        int tokens = CountTaggedCards(cards, DeckTags.Tokens) + CountTaggedCards(cards, DeckTags.SacrificeFodder);
        int graveyard = CountTaggedCards(cards, DeckTags.GraveyardHate) + CountTaggedCards(cards, DeckTags.Reanimation);
        int combo = CountTaggedCards(cards, DeckTags.ComboPiece) + CountTaggedCards(cards, DeckTags.ComboEnabler);
        int artifacts = cards
            .Where(card => DeckAnalysisMetrics.ContainsAny(DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "", "Artifact"))
            .Sum(card => Math.Max(1, card.Quantity));
        int enchantments = cards
            .Where(card => DeckAnalysisMetrics.ContainsAny(DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "", "Enchantment"))
            .Sum(card => Math.Max(1, card.Quantity));
        List<PlaygroupMetaPressureEvidence> pressures = [];
        AddImportedPressure(pressures, FastComboPressure, ramp >= 12 || tutors + combo >= 5, $"ramp {ramp}, tutors {tutors}, combo tags {combo}");
        AddImportedPressure(pressures, CreatureCombatPressure, creatures >= 24, $"creatures {creatures}");
        AddImportedPressure(pressures, GoWideTokensPressure, tokens >= 5, $"token tags {tokens}");
        AddImportedPressure(pressures, GraveyardRecursionPressure, graveyard >= 4, $"graveyard/reanimation tags {graveyard}");
        AddImportedPressure(pressures, StackControlPressure, interaction >= 14, $"interaction and wipes {interaction}");
        AddImportedPressure(pressures, StaxPressure, stax >= 3, $"stax tags {stax}");
        AddImportedPressure(pressures, ArtifactEnginePressure, artifacts >= 14, $"artifacts {artifacts}");
        AddImportedPressure(pressures, EnchantmentEnginePressure, enchantments >= 12, $"enchantments {enchantments}");
        return pressures;
    }

    /// <summary>
    /// Aggregates deck-level pressure evidence into weighted local-meta pressure.
    /// </summary>
    private static List<PlaygroupMetaPressureEvidence> AggregatePressures(IReadOnlyList<PlaygroupMetaDeckEvidence> decks)
    {
        Dictionary<string, (double Score, List<string> Evidence)> aggregate = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlaygroupMetaDeckEvidence deck in decks)
        {
            foreach (PlaygroupMetaPressureEvidence pressure in deck.Pressures)
            {
                double weighted = pressure.Score * deck.Weight;
                if (!aggregate.TryGetValue(pressure.Pressure, out (double Score, List<string> Evidence) current))
                {
                    current = (0, []);
                }

                current.Score += weighted;
                current.Evidence.AddRange(pressure.Evidence.Select(evidence => $"{deck.Name}: {evidence}"));
                aggregate[pressure.Pressure] = current;
            }
        }

        double totalDeckWeight = Math.Max(1, decks.Sum(deck => deck.Weight));
        return aggregate
            .Select(item => new PlaygroupMetaPressureEvidence
            {
                Pressure = item.Key,
                Score = Math.Clamp(item.Value.Score / totalDeckWeight, 0, 1),
                Source = "playgroup-aggregate",
                Evidence = item.Value.Evidence.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList(),
            })
            .Where(pressure => pressure.Score > 0)
            .OrderByDescending(pressure => pressure.Score)
            .ThenBy(pressure => pressure.Pressure, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
