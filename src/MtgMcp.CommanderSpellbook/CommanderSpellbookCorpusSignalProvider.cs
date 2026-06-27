using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.CommanderSpellbook;

/// <summary>
/// Produces combo and near-miss corpus signals from Commander Spellbook.
/// </summary>
public sealed class CommanderSpellbookCorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the combo lookup adapter used to reach Commander Spellbook.
    /// </summary>
    private readonly IComboCatalog comboCatalog;

    /// <summary>
    /// Stores source enablement and cache configuration.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a Commander Spellbook corpus provider.
    /// </summary>
    public CommanderSpellbookCorpusSignalProvider(
        IComboCatalog comboCatalog,
        IOptions<MtgMcpOptions> options)
    {
        this.comboCatalog = comboCatalog;
        this.options = options.Value;
    }

    /// <summary>
    /// Gets Commander Spellbook source capability and configuration status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        return new CorpusSourceStatus
        {
            Key = "commander-spellbook",
            Name = "Commander Spellbook",
            Kind = "combo-api",
            Enabled = sourceOptions.Enabled,
            StableApi = true,
            ApiType = CorpusSourceApiTypes.Official,
            Status = sourceOptions.Enabled ? CorpusSourceStatusKind.Available : CorpusSourceStatusKind.Disabled,
            AttributionRequired = true,
            Uri = "https://commanderspellbook.com/about/",
            Notes = ["Provides combo and near-miss data from the public find-my-combos endpoint."]
        };
    }

    /// <summary>
    /// Gets combo near-miss signals for the current deck card pool.
    /// </summary>
    public async Task<CorpusSignalReport> GetSignalsAsync(
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget,
        CancellationToken cancellationToken)
    {
        CorpusSourceStatus status = GetStatus();
        CorpusSignalReport report = new() { Sources = [status] };
        if (!status.Enabled || !budget.IncludeComboDetails || query.ExistingCards.Count == 0)
        {
            return report;
        }

        DeckComboReport combos = await comboCatalog
            .FindCombosAsync(
                new ComboCatalogQuery
                {
                    CardNames = query.ExistingCards,
                    Commander = query.Commander,
                    Format = query.Format,
                    Refresh = query.Refresh
                },
                cancellationToken)
            .ConfigureAwait(false);

        foreach (DeckCombo combo in combos.NearMisses.Where(combo => combo.MissingCards.Count > 0))
        {
            foreach (string missing in combo.MissingCards)
            {
                report.Signals.Add(new CardCorpusSignal
                {
                    CardName = missing,
                    Source = status.Name,
                    SignalType = CorpusSignalTypes.Combo,
                    Score = combo.Confidence,
                    Rationale = $"{missing} completes or advances combo route {combo.Name}: {combo.WinRoute}."
                });
            }
        }

        report.Notes.AddRange(combos.Notes);
        return report;
    }

    /// <summary>
    /// Gets configured Commander Spellbook corpus source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return options.Intelligence.Sources.TryGetValue("CommanderSpellbook", out MtgMcpCorpusSourceOptions? sourceOptions)
            ? sourceOptions
            : new MtgMcpCorpusSourceOptions();
    }
}
