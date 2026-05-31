namespace MtgMcp.Core;

/// <summary>
/// Classifies card and combo evidence into approved win-route labels.
/// </summary>
public static class WinRouteClassifier
{
    /// <summary>
    /// Classifies normalized produced features from a combo catalog.
    /// </summary>
    public static WinRouteClassification ClassifyProducedFeatures(
        string subject,
        IReadOnlyList<string> producedFeatures,
        SourceEvidenceMetadata? metadata = null)
    {
        WinRouteClassification classification = new()
        {
            Subject = subject,
            Evidence = producedFeatures
                .Where(feature => !string.IsNullOrWhiteSpace(feature))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Metadata = metadata ?? LocalMetadata("combo-produced-features")
        };
        string text = string.Join(" ", producedFeatures);
        ApplyTextRules(classification, text);
        NormalizeClassification(classification);
        return classification;
    }

    /// <summary>
    /// Classifies one card's oracle and type text.
    /// </summary>
    public static WinRouteClassification ClassifyCard(CardInfo card)
    {
        WinRouteClassification classification = new()
        {
            Subject = card.Name,
            Evidence =
            [
                card.TypeLine ?? "",
                card.OracleText ?? ""
            ],
            Metadata = new SourceEvidenceMetadata
            {
                Source = "scryfall",
                SourceKind = "card-facts",
                SourceUri = card.ScryfallUri,
                CacheStatus = "catalog",
                Confidence = 0.75,
                Deterministic = true,
                Notes = ["Route labels are deterministic text/facet classifications, not strategic recommendations."]
            }
        };
        ApplyTextRules(classification, $"{card.TypeLine} {card.OracleText}");
        NormalizeClassification(classification);
        return classification;
    }

    /// <summary>
    /// Applies route labels from a free-form evidence string.
    /// </summary>
    private static void ApplyTextRules(WinRouteClassification classification, string evidenceText)
    {
        string text = evidenceText.ToLowerInvariant();
        bool alternateWin =
            ContainsAny(text, "you win the game", "you win", "target player loses the game", "each opponent loses the game")
            || text.Contains("win the game", StringComparison.OrdinalIgnoreCase)
                && !ContainsAny(
                    text,
                    "can't win the game",
                    "cannot win the game",
                    "don't win the game",
                    "can't lose the game",
                    "cannot lose the game",
                    "don't lose the game");
        if (alternateWin)
        {
            AddRoute(classification, WinRouteLabels.AlternateWin, terminal: true);
        }

        if (ContainsAny(text, "each opponent loses", "opponent loses", "damage to each opponent", "deals damage to each opponent"))
        {
            AddRoute(classification, WinRouteLabels.Aristocrats, terminal: true);
        }

        if (ContainsAny(text, "mill each opponent", "each opponent mills", "target opponent mills", "opponents mill"))
        {
            AddRoute(classification, WinRouteLabels.OpponentMill, terminal: true);
        }

        if (ContainsAny(text, "infinite mana", "infinite colorless mana", "infinite colored mana"))
        {
            AddRoute(classification, WinRouteLabels.InfiniteMana, needsPayoff: true, payoffKind: "mana-sink");
        }

        if (ContainsAny(text, "storm count", "infinite storm", "copy target instant", "copy target sorcery"))
        {
            AddRoute(classification, WinRouteLabels.Storm, needsPayoff: true, payoffKind: "storm-payoff");
        }

        if (ContainsAny(text, "draw your deck", "draw any number", "draw cards equal", "draw your library", "infinite draw"))
        {
            AddRoute(classification, WinRouteLabels.DrawDeck, needsPayoff: true, payoffKind: "draw-deck-payoff");
        }

        if (ContainsAny(text, "mill yourself", "self mill", "mill your library", "put your library into your graveyard"))
        {
            AddRoute(classification, WinRouteLabels.SelfMill, needsPayoff: true, payoffKind: "self-mill-payoff");
        }

        if (ContainsAny(text, "extra turn", "extra turns", "additional turn"))
        {
            AddRoute(classification, WinRouteLabels.ExtraTurns, needsPayoff: true, payoffKind: "turn-loop-payoff");
        }

        if (ContainsAny(text, "dies", "sacrifice", "death trigger", "life drain", "drain"))
        {
            AddRoute(classification, WinRouteLabels.Aristocrats);
        }

        if (ContainsAny(text, "enters the battlefield", "etb", "enter the battlefield"))
        {
            AddRoute(classification, WinRouteLabels.Etb, needsPayoff: true, payoffKind: "etb-payoff");
        }

        if (ContainsAny(text, "token", "tokens", "infinite creatures"))
        {
            AddRoute(classification, WinRouteLabels.Tokens, needsPayoff: true, payoffKind: "combat-or-token-payoff");
        }

        if (ContainsAny(text, "attack", "combat damage", "trample", "extra combat"))
        {
            AddRoute(classification, WinRouteLabels.Combat);
        }
    }

    /// <summary>
    /// Adds one route and related terminal/payoff state.
    /// </summary>
    private static void AddRoute(
        WinRouteClassification classification,
        string route,
        bool terminal = false,
        bool needsPayoff = false,
        string? payoffKind = null)
    {
        classification.RouteTypes.Add(route);
        classification.Terminal |= terminal;
        classification.NeedsPayoff |= needsPayoff && !terminal;
        if (!string.IsNullOrWhiteSpace(payoffKind))
        {
            classification.PayoffKindsNeeded.Add(payoffKind);
        }
    }

    /// <summary>
    /// Removes duplicate labels and clears payoff flags for terminal routes.
    /// </summary>
    private static void NormalizeClassification(WinRouteClassification classification)
    {
        classification.RouteTypes = classification.RouteTypes
            .Where(route => WinRouteLabels.All.Contains(route, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        classification.PayoffKindsNeeded = classification.PayoffKindsNeeded
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (classification.Terminal)
        {
            classification.NeedsPayoff = false;
            classification.PayoffKindsNeeded.Clear();
        }
    }

    /// <summary>
    /// Creates local deterministic metadata for classifier-derived rows.
    /// </summary>
    private static SourceEvidenceMetadata LocalMetadata(string sourceKind)
    {
        return new SourceEvidenceMetadata
        {
            Source = "mtg-mcp",
            SourceKind = sourceKind,
            CacheStatus = "local",
            Confidence = 0.65,
            Deterministic = true,
            Notes = ["Route labels come from a fixed rule table over source evidence."]
        };
    }

    /// <summary>
    /// Checks whether text contains any phrase.
    /// </summary>
    private static bool ContainsAny(string value, params string[] phrases)
    {
        foreach (string phrase in phrases)
        {
            if (value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
