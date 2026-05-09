namespace MtgMcp.Scryfall;

public sealed class ScryfallOptions
{
    public Uri BaseAddress { get; set; } = new("https://api.scryfall.com/");
    public string UserAgent { get; set; } = "mtg-mcp/0.1 (+https://github.com/nccurry/mtg-mcp)";
    public TimeSpan MinimumDelay { get; set; } = TimeSpan.FromMilliseconds(75);
}
