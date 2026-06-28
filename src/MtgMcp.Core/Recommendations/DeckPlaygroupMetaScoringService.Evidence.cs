using System.Collections.Concurrent;

namespace MtgMcp.Core;

/// <summary>
/// Contains Playgroup meta-deck evidence import and pressure extraction orchestration.
/// </summary>
public sealed partial class DeckPlaygroupMetaScoringService
{
    /// <summary>
    /// Builds pressure evidence for ranked local-meta decks using bounded import parallelism.
    /// </summary>
    private async Task<List<PlaygroupMetaDeckEvidence>> BuildMetaDeckEvidenceBatchAsync(
        IReadOnlyList<PlaygroupDeckRanking> rankings,
        int rankingCount,
        CancellationToken cancellationToken)
    {
        ConcurrentDictionary<string, Lazy<Task<DeckWorkspace>>> importCache = new(StringComparer.OrdinalIgnoreCase);
        using SemaphoreSlim gate = new(MetaDeckEvidenceParallelism);

        async Task<PlaygroupMetaDeckEvidence> BuildWithGateAsync(PlaygroupDeckRanking ranking)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await BuildMetaDeckEvidenceAsync(
                        ranking,
                        rankingCount,
                        importCache,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        Task<PlaygroupMetaDeckEvidence>[] tasks = rankings.Select(BuildWithGateAsync).ToArray();
        PlaygroupMetaDeckEvidence[] evidence = await Task.WhenAll(tasks).ConfigureAwait(false);
        return evidence.ToList();
    }

    /// <summary>
    /// Builds pressure evidence for one ranked local-meta deck.
    /// </summary>
    private async Task<PlaygroupMetaDeckEvidence> BuildMetaDeckEvidenceAsync(
        PlaygroupDeckRanking ranking,
        int rankingCount,
        ConcurrentDictionary<string, Lazy<Task<DeckWorkspace>>> importCache,
        CancellationToken cancellationToken)
    {
        PlaygroupDeckSummary deck = ranking.Deck;
        List<string> warnings = [.. deck.Warnings];
        DeckWorkspace? imported = null;
        bool importedDecklist = false;
        if (IsArchidektDecklistUrl(deck.DecklistUrl))
        {
            if (archidektGateway is null)
            {
                warnings.Add("Archidekt decklist URL was present, but no Archidekt gateway is configured.");
            }
            else
            {
                try
                {
                    imported = await ImportArchidektDecklistAsync(
                            deck.DecklistUrl!,
                            importCache,
                            cancellationToken)
                        .ConfigureAwait(false);
                    importedDecklist = true;
                }
                catch (Exception exception) when (!DeckServiceHelpers.IsCancellation(exception))
                {
                    warnings.Add($"Archidekt decklist import failed: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        double confidence = DeckEvidenceConfidence(deck, importedDecklist);
        double rankWeight = rankingCount <= 1
            ? 1
            : 1 - ((ranking.Rank - 1) / (double)Math.Max(1, rankingCount)) * 0.35;
        List<PlaygroupMetaPressureEvidence> pressures = InferDeckPressures(deck, imported);
        return new PlaygroupMetaDeckEvidence
        {
            DeckId = deck.DeckId,
            Name = deck.Name,
            OwnerName = deck.OwnerName,
            CommanderNames = deck.CommanderNames.ToList(),
            RankingScore = ranking.Score,
            Weight = Math.Clamp(rankWeight * confidence, 0.2, 1),
            ImportedDecklist = importedDecklist,
            DecklistUrl = deck.DecklistUrl,
            Confidence = confidence,
            Pressures = pressures,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Imports an Archidekt decklist once per scoring request, sharing duplicate URL lookups.
    /// </summary>
    private Task<DeckWorkspace> ImportArchidektDecklistAsync(
        string decklistUrl,
        ConcurrentDictionary<string, Lazy<Task<DeckWorkspace>>> importCache,
        CancellationToken cancellationToken)
    {
        IArchidektGateway gateway = archidektGateway
            ?? throw new InvalidOperationException("Archidekt gateway is not configured.");
        Lazy<Task<DeckWorkspace>> importTask = importCache.GetOrAdd(
            decklistUrl,
            url => new Lazy<Task<DeckWorkspace>>(
                () => gateway.ImportDeckAsync(url, writeBack: false, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return importTask.Value;
    }
}
