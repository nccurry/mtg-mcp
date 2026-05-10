namespace MtgMcp.CommanderSpellbook;

/// <summary>
/// Configures Commander Spellbook HTTP access.
/// </summary>
public sealed class CommanderSpellbookOptions
{
    /// <summary>
    /// Gets or sets the Commander Spellbook backend base address.
    /// </summary>
    public Uri BaseAddress { get; set; } = new("https://backend.commanderspellbook.com/");
}
