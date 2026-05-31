namespace MtgMcp.Core;

/// <summary>
/// Describes source and determinism metadata attached to evidence-like results.
/// </summary>
public sealed class SourceEvidenceMetadata
{
    /// <summary>
    /// Gets or sets the source key or display name.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the kind of source population represented by the evidence.
    /// </summary>
    public string SourceKind { get; set; } = "";

    /// <summary>
    /// Gets or sets a source page or API URL suitable for attribution.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Gets or sets when the source evidence was retrieved or assembled.
    /// </summary>
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets whether the evidence was live, cached, unavailable, or locally derived.
    /// </summary>
    public string CacheStatus { get; set; } = "live";

    /// <summary>
    /// Gets or sets the source-specific confidence from 0 to 1.
    /// </summary>
    public double Confidence { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether the row was produced by deterministic rules rather than LLM inference.
    /// </summary>
    public bool Deterministic { get; set; } = true;

    /// <summary>
    /// Gets or sets source limitations and non-fatal caveats.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

