using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Configures scryfall options settings.
/// </summary>
public sealed class ScryfallOptions
{
    /// <summary>
    /// Gets or sets the base address.
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https://api.scryfall.com/");

    /// <summary>
    /// Gets or sets the user agent.
    /// </summary>
    public string UserAgent { get; set; } = MtgMcpHttpDefaults.UserAgent;

    /// <summary>
    /// Gets or sets the minimum delay.
    /// </summary>
    public TimeSpan MinimumDelay { get; set; } = TimeSpan.FromMilliseconds(125);

    /// <summary>
    /// Gets or sets how many Scryfall 429 responses are retried before surfacing the failure.
    /// </summary>
    public int MaxRateLimitRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the release-date reference used for deterministic pricing tests and replayable fetches.
    /// </summary>
    public DateOnly? PricingReferenceDate { get; set; }

    /// <summary>
    /// Gets or sets how named-card results are replaced with price-relevant printings.
    /// </summary>
    public PricingMode PricingMode { get; set; } = PricingMode.ReleasedIfNeeded;

    /// <summary>
    /// Gets or sets the format legality checked by budget-playable pricing when source data includes it.
    /// </summary>
    public string? PricingFormat { get; set; } = "commander";

    /// <summary>
    /// Gets or sets whether budget-playable pricing may use foil, etched, or market fallback prices.
    /// </summary>
    public bool AllowAnyFinishForBudgetPricing { get; set; }
}
