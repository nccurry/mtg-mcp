using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Persists deck edit plans as json files.
/// </summary>
public sealed class JsonDeckPlanRepository : IDeckPlanRepository
{
    /// <summary>
    /// Stores serializer options.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Stores the plan directory.
    /// </summary>
    private readonly string planDirectory;

    /// <summary>
    /// Handles json deck plan Repository.
    /// </summary>
    public JsonDeckPlanRepository(string dataDirectory)
    {
        planDirectory = Path.Combine(dataDirectory, "plans");
    }

    /// <summary>
    /// Saves the plan.
    /// </summary>
    public async Task<DeckEditPlan> SaveAsync(DeckEditPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Directory.CreateDirectory(planDirectory);

        string path = GetPlanPath(plan.PlanId);
        string tempPath = Path.Combine(planDirectory, $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, plan, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return plan;
    }

    /// <summary>
    /// Gets the plan.
    /// </summary>
    public async Task<DeckEditPlan?> GetAsync(string planId, CancellationToken cancellationToken)
    {
        string path = GetPlanPath(planId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DeckEditPlan>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the plans.
    /// </summary>
    public async Task<IReadOnlyList<DeckEditPlan>> ListAsync(
        string? workspaceId,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(planDirectory))
        {
            return [];
        }

        List<DeckEditPlan> plans = [];
        foreach (string path in Directory.EnumerateFiles(planDirectory, "*.json"))
        {
            await using FileStream stream = File.OpenRead(path);
            DeckEditPlan? plan = await JsonSerializer.DeserializeAsync<DeckEditPlan>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (plan is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(workspaceId)
                && !string.Equals(plan.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            plans.Add(plan);
        }

        return plans
            .OrderByDescending(plan => plan.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Deletes the plan and reports whether one existed.
    /// </summary>
    public Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken)
    {
        string path = GetPlanPath(planId);
        bool exists = File.Exists(path);
        if (exists)
        {
            File.Delete(path);
        }

        return Task.FromResult(exists);
    }

    /// <summary>
    /// Gets the plan path.
    /// </summary>
    private string GetPlanPath(string planId)
    {
        string safeId = string.Concat(planId.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeId))
        {
            throw new ArgumentException("Plan id must contain at least one alphanumeric character.", nameof(planId));
        }

        return Path.Combine(planDirectory, $"{safeId}.json");
    }
}
