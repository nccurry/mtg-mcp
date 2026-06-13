using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Stores the most recent process-local Reddit source health observation for source_list.
/// </summary>
public sealed class RedditSourceHealth
{
    /// <summary>
    /// Guards access to the last observed status fields.
    /// </summary>
    private readonly object sync = new();

    /// <summary>
    /// Stores the last observed Reddit status label.
    /// </summary>
    private string? status;

    /// <summary>
    /// Stores when the last live Reddit source call observed the status.
    /// </summary>
    private DateTimeOffset? checkedAt;

    /// <summary>
    /// Stores concise setup or failure notes from the last live observation.
    /// </summary>
    private List<string> notes = [];

    /// <summary>
    /// Remembers one live Reddit source status observation.
    /// </summary>
    public void Remember(string status, DateTimeOffset checkedAt, IReadOnlyList<string> notes)
    {
        lock (sync)
        {
            this.status = status;
            this.checkedAt = checkedAt;
            this.notes = [.. notes];
        }
    }

    /// <summary>
    /// Gets the last live source observation when one exists.
    /// </summary>
    public bool TryGetLastObservation(
        out string status,
        out DateTimeOffset checkedAt,
        out IReadOnlyList<string> notes)
    {
        lock (sync)
        {
            if (string.IsNullOrWhiteSpace(this.status) || this.checkedAt is null)
            {
                status = "";
                checkedAt = default;
                notes = [];
                return false;
            }

            status = this.status;
            checkedAt = this.checkedAt.Value;
            notes = [.. this.notes];
            return true;
        }
    }

    /// <summary>
    /// Clears the last observation when a credentialed source path supersedes public JSON health.
    /// </summary>
    public void Clear()
    {
        lock (sync)
        {
            status = null;
            checkedAt = null;
            notes = [];
        }
    }
}
