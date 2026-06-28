using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Persists JSON documents in one directory with atomic writes and collision-resistant id paths.
/// </summary>
internal sealed class JsonFileStore<T>
{
    /// <summary>
    /// Uses the same JSON formatting as the previous workspace and plan repositories.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Stores all JSON documents for this entity type.
    /// </summary>
    private readonly string directory;

    /// <summary>
    /// Names the persisted entity in validation errors.
    /// </summary>
    private readonly string entityName;

    /// <summary>
    /// Reads the stable id from a deserialized legacy document before deleting it.
    /// </summary>
    private readonly Func<T, string>? getId;

    /// <summary>
    /// Creates a store rooted at a concrete entity directory.
    /// </summary>
    public JsonFileStore(string directory, string entityName, Func<T, string>? getId = null)
    {
        this.directory = directory;
        this.entityName = entityName;
        this.getId = getId;
    }

    /// <summary>
    /// Saves an entity document under the supplied stable id.
    /// </summary>
    public async Task<T> SaveAsync(string id, T value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        Directory.CreateDirectory(directory);

        string path = GetPrimaryPath(id);
        string tempPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite: true);
            DeleteLegacyPathAfterSuccessfulSave(id, path);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        return value;
    }

    /// <summary>
    /// Loads an entity document by id, including legacy sanitized filenames written by older releases.
    /// </summary>
    public async Task<T?> GetAsync(string id, CancellationToken cancellationToken)
    {
        foreach (string path in GetCandidatePaths(id))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        return default;
    }

    /// <summary>
    /// Lists every JSON entity document in this store.
    /// </summary>
    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<T> values = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            await using FileStream stream = File.OpenRead(path);
            T? value = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            if (value is not null)
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// Deletes an entity document by id from both current and legacy path shapes.
    /// </summary>
    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool deleted = false;
        string primaryPath = GetPrimaryPath(id);
        if (File.Exists(primaryPath))
        {
            File.Delete(primaryPath);
            deleted = true;
        }

        string legacyPath = GetLegacyPath(id);
        if (!legacyPath.Equals(primaryPath, StringComparison.Ordinal)
            && File.Exists(legacyPath)
            && LegacyPathBelongsToId(legacyPath, id))
        {
            File.Delete(legacyPath);
            deleted = true;
        }

        return Task.FromResult(deleted);
    }

    /// <summary>
    /// Returns the current collision-safe path before the legacy sanitized fallback path.
    /// </summary>
    private IEnumerable<string> GetCandidatePaths(string id)
    {
        string primaryPath = GetPrimaryPath(id);
        yield return primaryPath;

        string legacyPath = GetLegacyPath(id);
        if (!legacyPath.Equals(primaryPath, StringComparison.Ordinal))
        {
            yield return legacyPath;
        }
    }

    /// <summary>
    /// Builds the canonical path for an id, preserving legacy paths for already-safe ids.
    /// </summary>
    private string GetPrimaryPath(string id)
    {
        string legacySafeId = BuildLegacySafeId(id);
        if (legacySafeId.Equals(id, StringComparison.Ordinal))
        {
            return Path.Combine(directory, $"{legacySafeId}.json");
        }

        string prefix = legacySafeId.Length <= 48 ? legacySafeId : legacySafeId[..48];
        return Path.Combine(directory, $"{prefix}-{HashId(id)}.json");
    }

    /// <summary>
    /// Builds the filename shape used by earlier releases before id hashing was added.
    /// </summary>
    private string GetLegacyPath(string id)
    {
        return Path.Combine(directory, $"{BuildLegacySafeId(id)}.json");
    }

    /// <summary>
    /// Produces the alphanumeric id component accepted by the legacy filename strategy.
    /// </summary>
    private string BuildLegacySafeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        string safeId = string.Concat(id.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeId))
        {
            throw new ArgumentException(
                $"{entityName} id must contain at least one alphanumeric character.",
                nameof(id));
        }

        return safeId;
    }

    /// <summary>
    /// Removes an old legacy file after a successful write when it belongs to the saved id.
    /// </summary>
    private void DeleteLegacyPathAfterSuccessfulSave(string id, string primaryPath)
    {
        string legacyPath = GetLegacyPath(id);
        if (legacyPath.Equals(primaryPath, StringComparison.Ordinal) || !File.Exists(legacyPath))
        {
            return;
        }

        if (LegacyPathBelongsToId(legacyPath, id))
        {
            File.Delete(legacyPath);
        }
    }

    /// <summary>
    /// Checks legacy file ownership before deleting paths that may collide after sanitization.
    /// </summary>
    private bool LegacyPathBelongsToId(string legacyPath, string id)
    {
        if (getId is null)
        {
            return true;
        }

        try
        {
            using FileStream stream = File.OpenRead(legacyPath);
            T? legacyValue = JsonSerializer.Deserialize<T>(stream, SerializerOptions);
            return legacyValue is not null && getId(legacyValue).Equals(id, StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a short stable suffix so non-alphanumeric ids do not collapse to the same file.
    /// </summary>
    private static string HashId(string id)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(id);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
