namespace MtgMcp.Core;

/// <summary>
/// Provides bounded Commander candidate discovery from catalog and corpus evidence.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Finds Commander candidates with EDHREC eligible deck counts inside requested bounds.
    /// </summary>
    public async Task<CommanderCandidateSearchResult> SearchCommanderCandidatesAsync(
        string? colorIdentity,
        bool exactColorIdentity,
        int minEligibleDecks,
        int? maxEligibleDecks,
        int limit,
        int scryfallCandidateCap,
        int edhrecFetchCap,
        bool refresh,
        CancellationToken cancellationToken)
    {
        int boundedLimit = Math.Clamp(limit, 1, 50);
        int boundedCandidateCap = Math.Clamp(scryfallCandidateCap, 1, 200);
        int boundedFetchCap = Math.Clamp(edhrecFetchCap, 1, 50);
        int boundedMin = Math.Max(0, minEligibleDecks);
        int? boundedMax = maxEligibleDecks.HasValue
            ? Math.Max(boundedMin, maxEligibleDecks.Value)
            : null;
        string? normalizedColorIdentity = NormalizeColorIdentityFilter(colorIdentity);
        CommanderCandidateSearchResult result = new()
        {
            ColorIdentity = normalizedColorIdentity ?? "",
            ExactColorIdentity = exactColorIdentity,
            MinEligibleDecks = boundedMin,
            MaxEligibleDecks = boundedMax,
            ScryfallCandidateCap = boundedCandidateCap,
            EdhrecFetchCap = boundedFetchCap,
            Notes =
            [
                "Commander discovery uses bounded catalog search followed by bounded EDHREC aggregate evidence lookups.",
                "EDHREC evidence is unofficial, uses the configured corpus cache unless source refresh is requested, and may return partial results."
            ]
        };
        if (normalizedColorIdentity is null)
        {
            result.Notes.Add("No color identity filter was applied.");
        }
        else if (string.IsNullOrEmpty(normalizedColorIdentity))
        {
            result.Notes.Add("Colorless commander candidate filter was applied.");
        }
        else
        {
            result.Notes.Add($"Commander candidate color identity filter: {normalizedColorIdentity}.");
        }

        IReadOnlyList<CardSearchResult> searchedCandidates = await CardCatalog
            .SearchCardsAsync(
                CardSearchRequest.CommanderCandidates(normalizedColorIdentity, exactColorIdentity),
                boundedCandidateCap,
                cancellationToken)
            .ConfigureAwait(false);
        List<CardSearchResult> candidates = searchedCandidates
            .Take(boundedCandidateCap)
            .ToList();
        result.ScryfallCandidatesInspected = candidates.Count;
        IReadOnlyDictionary<string, CardInfo> cardDetails = await CardCatalog
            .GetCardsByNamesAsync(
                candidates.Select(candidate => candidate.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                cancellationToken)
            .ConfigureAwait(false);

        int fetched = 0;
        foreach (CardSearchResult candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fetched >= boundedFetchCap || result.Commanders.Count >= boundedLimit)
            {
                break;
            }

            if (!cardDetails.TryGetValue(candidate.Name, out CardInfo? card))
            {
                result.Notes.Add($"Skipped {candidate.Name}: card metadata was unavailable.");
                continue;
            }

            fetched++;
            result.EdhrecFetchesAttempted = fetched;
            CommanderAggregateCardsResult aggregate;
            try
            {
                aggregate = await GetCommanderAggregateCardsAsync(
                        card.Name,
                        theme: null,
                        source: "edhrec",
                        limit: 1,
                        refresh,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.Notes.Add($"Skipped {card.Name}: EDHREC lookup failed: {exception.Message}");
                continue;
            }

            result.Sources.AddRange(aggregate.Sources);
            result.Notes.AddRange(aggregate.Notes);
            int? eligibleDeckCount = aggregate.Cards
                .Select(row => row.EligibleDeckCount)
                .FirstOrDefault(count => count.HasValue);
            if (!eligibleDeckCount.HasValue)
            {
                result.Notes.Add($"Skipped {card.Name}: EDHREC eligible deck count was unavailable.");
                continue;
            }

            if (eligibleDeckCount.Value < boundedMin
                || (boundedMax.HasValue && eligibleDeckCount.Value > boundedMax.Value))
            {
                continue;
            }

            result.Commanders.Add(new CommanderCandidateRow
            {
                CommanderName = card.Name,
                ColorIdentity = card.ColorIdentity.ToList(),
                EligibleDeckCount = eligibleDeckCount,
                ScryfallUri = card.ScryfallUri,
                EdhrecUri = aggregate.Cards
                    .Select(row => row.Metadata.SourceUri)
                    .FirstOrDefault(uri => !string.IsNullOrWhiteSpace(uri)),
            });
        }

        result.Sources = CorpusSourceStatusHelpers.MergeSourceStatuses(result.Sources);
        if (fetched >= boundedFetchCap && result.Commanders.Count < boundedLimit)
        {
            result.Notes.Add("EDHREC fetch cap was reached before filling the requested result limit.");
        }

        return result;
    }

    /// <summary>
    /// Normalizes a color identity string to WUBRG order.
    /// </summary>
    private static string? NormalizeColorIdentityFilter(string? colorIdentity)
    {
        if (colorIdentity is null)
        {
            return null;
        }

        string trimmed = colorIdentity.Trim();
        if (trimmed.Length == 0
            || trimmed.Equals("C", StringComparison.OrdinalIgnoreCase)
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
}
