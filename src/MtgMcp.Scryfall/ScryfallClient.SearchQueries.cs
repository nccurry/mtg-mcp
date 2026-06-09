using System.Globalization;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Contains provider-neutral search request translation for ScryfallClient.
/// </summary>
public sealed partial class ScryfallClient
{
    /// <summary>
    /// Converts a provider-neutral search request into Scryfall search syntax.
    /// </summary>
    private static string BuildSearchQuery(CardSearchRequest request)
    {
        string format = NormalizeFormat(request.Format);
        return request.Preset switch
        {
            CardSearchPreset.RawQuery => BuildRawSearchQuery(request, format),
            CardSearchPreset.CommanderGameChangers => "is:game-changer",
            CardSearchPreset.CommanderCandidates => BuildCommanderCandidateQuery(request, format),
            CardSearchPreset.Role => BuildRoleSearchQuery(request.Role, format, request.MaxPrice),
            CardSearchPreset.CommanderProtectionEquipment =>
                BuildFilteredQuery("(o:equipped o:hexproof or o:equipped o:shroud or (o:target o:creature o:hexproof) or (o:permanents o:control o:hexproof))", format, request.MaxPrice),
            CardSearchPreset.CommanderProtectionSpell =>
                BuildFilteredQuery("((o:\"creature you control\" o:indestructible) or (o:target o:creature o:protection) or o:\"phase out\")", format, request.MaxPrice),
            CardSearchPreset.DrawDiscard =>
                BuildFilteredQuery("(o:draw or o:\"draw a card\" or o:\"each opponent discards\" or o:\"each player discards\" or o:\"whenever an opponent discards\" or o:\"whenever you discard\" or o:\"discard a card\")", format, request.MaxPrice),
            CardSearchPreset.CardDraw =>
                BuildFilteredQuery("(o:draw or o:\"draw a card\" or o:\"draw cards\")", format, request.MaxPrice),
            CardSearchPreset.DiscardSynergy =>
                BuildFilteredQuery("(o:\"each opponent discards\" or o:\"each player discards\" or o:\"target player discards\" or o:\"whenever an opponent discards\" or o:\"whenever you discard\")", format, request.MaxPrice),
            CardSearchPreset.PoliticalChoices =>
                BuildFilteredQuery("(o:goad or o:monarch or o:vote or o:\"council's dilemma\" or o:\"will of the council\" or o:\"tempting offer\")", format, request.MaxPrice),
            CardSearchPreset.PoliticalTableEffects =>
                BuildFilteredQuery("(o:\"each opponent\" or o:\"opponents choose\" or o:\"each player votes\")", format, request.MaxPrice),
            CardSearchPreset.WholeTablePolitics =>
                BuildFilteredQuery("(o:goad or o:monarch or o:vote or o:\"tempting offer\" or o:\"each opponent\")", format, request.MaxPrice),
            CardSearchPreset.WholeTableEffects =>
                BuildFilteredQuery("(o:\"each player\" or o:\"opponents choose\" or o:\"each creature\")", format, request.MaxPrice),
            CardSearchPreset.TableWideInteraction =>
                BuildFilteredQuery("(o:\"each opponent\" or o:\"opponents choose\" or o:\"each player votes\" or o:\"each player\" or o:\"each creature\")", format, request.MaxPrice),
            CardSearchPreset.TokenDefenseSweepers =>
                BuildFilteredQuery("(o:\"destroy all tokens\" or o:\"each creature gets -1/-1\" or o:\"prevent all combat damage\")", format, request.MaxPrice),
            CardSearchPreset.TokenDefensePillowfort =>
                BuildFilteredQuery("(o:\"creatures can't attack you\" or o:\"unless their controller pays\")", format, request.MaxPrice),
            CardSearchPreset.GraveyardHate =>
                BuildFilteredQuery("(o:\"exile target card from a graveyard\" or o:\"exile all graveyards\" or o:\"cards in graveyards\")", format, request.MaxPrice),
            CardSearchPreset.Finishers =>
                BuildFilteredQuery("(o:\"each opponent loses\" or o:\"damage to each opponent\" or o:\"win the game\" or o:\"extra combat\")", format, request.MaxPrice),
            CardSearchPreset.LessSaltyValue =>
                BuildFilteredQuery("(o:create or o:draw or o:gain)", format, request.MaxPrice),
            CardSearchPreset.BroadUseful => BuildFilteredQuery("", format, request.MaxPrice),
            CardSearchPreset.BroadUsefulFallback =>
                BuildFilteredQuery("(o:draw or o:\"destroy target\" or o:add)", format, request.MaxPrice),
            CardSearchPreset.RecentCards => BuildRecentSearchQuery(request, format),
            _ => BuildFilteredQuery("", format, request.MaxPrice)
        };
    }

    /// <summary>
    /// Builds a legal commander search query with optional color-identity filtering.
    /// </summary>
    private static string BuildCommanderCandidateQuery(CardSearchRequest request, string format)
    {
        List<string> parts =
        [
            "(is:commander or (t:legendary t:creature))",
            $"legal:{format}"
        ];
        if (request.ColorIdentity is not null)
        {
            string colors = NormalizeColorIdentity(request.ColorIdentity);
            if (string.IsNullOrWhiteSpace(colors))
            {
                parts.Add(request.ExactColorIdentity ? "ci=c" : "ci<=c");
            }
            else
            {
                parts.Add(request.ExactColorIdentity ? $"ci={colors}" : $"ci<={colors}");
            }
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Adds search-side legality and budget hints around a caller-supplied query.
    /// </summary>
    private static string BuildRawSearchQuery(CardSearchRequest request, string format)
    {
        string query = request.RawQuery ?? "";
        List<string> parts = [];
        bool addLegality = !query.Contains("legal:", StringComparison.OrdinalIgnoreCase);
        decimal? maxPrice = request.MaxPrice;
        bool addBudget = maxPrice.HasValue
            && !ContainsAny(query, "usd<", "usd>", "eur<", "eur>", "tix<", "tix>");
        if (!string.IsNullOrWhiteSpace(query))
        {
            parts.Add(addLegality || addBudget ? $"({query.Trim()})" : query.Trim());
        }

        if (addLegality)
        {
            parts.Add($"legal:{format}");
        }

        if (addBudget)
        {
            parts.Add(PriceFragment(maxPrice.GetValueOrDefault()));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Builds a role-oriented search query for Scryfall.
    /// </summary>
    private static string BuildRoleSearchQuery(string? role, string format, decimal? maxPrice)
    {
        string roleQuery = (role ?? "").ToLowerInvariant() switch
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

        return BuildFilteredQuery(roleQuery, format, maxPrice);
    }

    /// <summary>
    /// Builds a recent-card search query for Scryfall.
    /// </summary>
    private static string BuildRecentSearchQuery(CardSearchRequest request, string format)
    {
        List<string> parts = [$"legal:{format}"];
        if (request.Since.HasValue)
        {
            parts.Add($"date>={request.Since.Value:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(request.SetCode))
        {
            parts.Add($"set:{request.SetCode}");
        }

        if (request.MaxPrice.HasValue)
        {
            parts.Add(PriceFragment(request.MaxPrice.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Theme))
        {
            parts.Add(ThemeSearchFragment(request.Theme));
        }

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// Adds common legality and price filters to an optional Scryfall expression.
    /// </summary>
    private static string BuildFilteredQuery(string expression, string format, decimal? maxPrice)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(expression))
        {
            parts.Add(expression);
        }

        parts.Add($"legal:{format}");
        if (maxPrice.HasValue)
        {
            parts.Add(PriceFragment(maxPrice.Value));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Builds a rough Scryfall text fragment from theme text.
    /// </summary>
    private static string ThemeSearchFragment(string theme)
    {
        string normalized = theme.ToLowerInvariant();
        if (normalized.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:create o:token)";
        }

        if (normalized.Contains("discard", StringComparison.OrdinalIgnoreCase))
        {
            return "o:discard";
        }

        if (normalized.Contains("grave", StringComparison.OrdinalIgnoreCase) || normalized.Contains("reanim", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:graveyard or o:reanimate or o:\"return target creature\")";
        }

        if (normalized.Contains("aristocrat", StringComparison.OrdinalIgnoreCase) || normalized.Contains("sacrifice", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:sacrifice or o:\"whenever a creature dies\")";
        }

        return "";
    }

    /// <summary>
    /// Normalizes empty and EDH format aliases for Scryfall legality filters.
    /// </summary>
    private static string NormalizeFormat(string? format)
    {
        string normalized = format?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "" => "commander",
            "edh" => "commander",
            _ => normalized
        };
    }

    /// <summary>
    /// Normalizes color identity text to Scryfall's WUBRG order.
    /// </summary>
    private static string NormalizeColorIdentity(string? colorIdentity)
    {
        if (string.IsNullOrWhiteSpace(colorIdentity))
        {
            return "";
        }

        string trimmed = colorIdentity.Trim();
        if (trimmed.Equals("C", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("colorless", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        List<char> colors = [];
        foreach (char color in "WUBRG")
        {
            if (trimmed.Contains(color, StringComparison.OrdinalIgnoreCase))
            {
                colors.Add(color);
            }
        }

        return new string(colors.ToArray());
    }

    /// <summary>
    /// Formats a USD price filter using invariant culture.
    /// </summary>
    private static string PriceFragment(decimal maxPrice)
    {
        return $"usd<={maxPrice.ToString("0.##", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Checks whether text contains any supplied phrase.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

}
