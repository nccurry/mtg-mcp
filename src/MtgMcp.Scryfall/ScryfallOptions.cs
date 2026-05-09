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
    public string UserAgent { get; set; } = "mtg-mcp/0.1 (+https://github.com/nccurry/mtg-mcp)";

    /// <summary>
    /// Gets or sets the minimum delay.
    /// </summary>
    public TimeSpan MinimumDelay { get; set; } = TimeSpan.FromMilliseconds(75);
}
