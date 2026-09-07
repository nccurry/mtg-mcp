namespace MtgMcp.Spellbook;

/// <summary>
/// Holds local cache settings for Commander Spellbook source evidence.
/// </summary>
public sealed record SpellbookOptions(string DataRoot, TimeSpan CacheTtl)
{
    /// <summary>
    /// Gets the normal freshness limit for a successful source response.
    /// </summary>
    public static TimeSpan DefaultCacheTtl { get; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Creates supported local cache settings with a full local data-root path.
    /// </summary>
    public static SpellbookOptions CreateDefault(string dataRoot, TimeSpan? cacheTtl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return new SpellbookOptions(Path.GetFullPath(dataRoot), cacheTtl ?? DefaultCacheTtl);
    }

    /// <summary>
    /// Rejects cache settings outside the documented whole-minute boundary.
    /// </summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DataRoot);
        if (CacheTtl < TimeSpan.FromMinutes(1) || CacheTtl > TimeSpan.FromHours(24) ||
            CacheTtl.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CacheTtl),
                "Commander Spellbook cache freshness must be whole minutes from 1 through 1440.");
        }
    }
}
