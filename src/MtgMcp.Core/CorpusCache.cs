using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Provides corpus cache creation and duration parsing.
/// </summary>
public static class CorpusCacheFactory
{
    /// <summary>
    /// Creates the configured source-fact cache.
    /// </summary>
    public static ICorpusCache Create(string dataDir, MtgMcpCorpusCacheOptions options)
    {
        string mode = NormalizeMode(options.Mode);
        return mode switch
        {
            CorpusCacheModes.Off => new NullCorpusCache(),
            CorpusCacheModes.Memory => new MemoryCorpusCache(options),
            _ => new FileCorpusCache(Path.Combine(dataDir, "corpus-cache"), options)
        };
    }

    /// <summary>
    /// Parses compact duration strings such as 6h, 24h, and 7d.
    /// </summary>
    public static TimeSpan ParseDuration(string? value, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string trimmed = value.Trim();
        if (TimeSpan.TryParse(trimmed, out TimeSpan parsed))
        {
            return parsed;
        }

        char suffix = char.ToLowerInvariant(trimmed[^1]);
        string number = char.IsLetter(suffix) ? trimmed[..^1] : trimmed;
        if (!double.TryParse(number, out double amount) || amount <= 0)
        {
            return fallback;
        }

        return suffix switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => fallback
        };
    }

    /// <summary>
    /// Normalizes cache mode values.
    /// </summary>
    private static string NormalizeMode(string? mode)
    {
        return string.IsNullOrWhiteSpace(mode)
            ? CorpusCacheModes.Persisted
            : mode.Trim().ToLowerInvariant() switch
            {
                "none" or "disabled" or "disable" => CorpusCacheModes.Off,
                CorpusCacheModes.Off => CorpusCacheModes.Off,
                CorpusCacheModes.Memory => CorpusCacheModes.Memory,
                _ => CorpusCacheModes.Persisted
            };
    }
}

/// <summary>
/// Disables source-fact caching.
/// </summary>
public sealed class NullCorpusCache : ICorpusCache
{
    /// <summary>
    /// Always returns a cache miss.
    /// </summary>
    public Task<T?> GetAsync<T>(
        CorpusCacheKey key,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<T?>(default);
    }

    /// <summary>
    /// Ignores stored values.
    /// </summary>
    public Task SetAsync<T>(
        CorpusCacheKey key,
        T value,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stores source facts in memory for one server process.
/// </summary>
public sealed class MemoryCorpusCache : ICorpusCache
{
    /// <summary>
    /// Stores JSON serialization settings.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores serialized cache entries by stable key.
    /// </summary>
    private readonly ConcurrentDictionary<string, MemoryCorpusCacheEntry> entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores the maximum entry count.
    /// </summary>
    private readonly int maxEntries;

    /// <summary>
    /// Creates a memory cache with configured bounds.
    /// </summary>
    public MemoryCorpusCache(MtgMcpCorpusCacheOptions options)
    {
        maxEntries = Math.Max(1, options.MaxEntries);
    }

    /// <summary>
    /// Gets a fresh memory cache entry by key.
    /// </summary>
    public Task<T?> GetAsync<T>(
        CorpusCacheKey key,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        string cacheKey = StableKey(key);
        if (!entries.TryGetValue(cacheKey, out MemoryCorpusCacheEntry? entry))
        {
            return Task.FromResult<T?>(default);
        }

        if (DateTimeOffset.UtcNow - entry.StoredAt > timeToLive)
        {
            entries.TryRemove(cacheKey, out _);
            return Task.FromResult<T?>(default);
        }

        entry.LastAccessedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Json, JsonOptions));
    }

    /// <summary>
    /// Stores a memory cache entry and prunes old entries.
    /// </summary>
    public Task SetAsync<T>(
        CorpusCacheKey key,
        T value,
        CancellationToken cancellationToken)
    {
        if (entries.Count >= maxEntries)
        {
            foreach (string staleKey in entries
                .OrderBy(pair => pair.Value.LastAccessedAt)
                .Take(Math.Max(1, entries.Count - maxEntries + 1))
                .Select(pair => pair.Key))
            {
                entries.TryRemove(staleKey, out _);
            }
        }

        entries[StableKey(key)] = new MemoryCorpusCacheEntry(
            JsonSerializer.Serialize(value, JsonOptions),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a stable in-memory key string.
    /// </summary>
    private static string StableKey(CorpusCacheKey key)
    {
        return string.Join('\u001f', key.Source, key.Endpoint, key.Query, key.AdapterVersion, key.Budget);
    }

    /// <summary>
    /// Stores one serialized in-memory cache entry.
    /// </summary>
    private sealed class MemoryCorpusCacheEntry(
        string json,
        DateTimeOffset storedAt,
        DateTimeOffset lastAccessedAt)
    {
        /// <summary>
        /// Gets the serialized value.
        /// </summary>
        public string Json { get; } = json;

        /// <summary>
        /// Gets when the value was stored.
        /// </summary>
        public DateTimeOffset StoredAt { get; } = storedAt;

        /// <summary>
        /// Gets or sets when the value was last read.
        /// </summary>
        public DateTimeOffset LastAccessedAt { get; set; } = lastAccessedAt;
    }
}

/// <summary>
/// Stores source facts as JSON files under the mtg-mcp data directory.
/// </summary>
public sealed class FileCorpusCache : ICorpusCache
{
    /// <summary>
    /// Stores JSON serialization settings.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores the cache directory path.
    /// </summary>
    private readonly string cacheDirectory;

    /// <summary>
    /// Stores the maximum cache size in bytes.
    /// </summary>
    private readonly long maxBytes;

    /// <summary>
    /// Stores the maximum entry count.
    /// </summary>
    private readonly int maxEntries;

    /// <summary>
    /// Creates a persisted source-fact cache.
    /// </summary>
    public FileCorpusCache(string cacheDirectory, MtgMcpCorpusCacheOptions options)
    {
        this.cacheDirectory = cacheDirectory;
        maxBytes = Math.Max(1, options.MaxBytes);
        maxEntries = Math.Max(1, options.MaxEntries);
    }

    /// <summary>
    /// Gets a fresh persisted cache entry by key.
    /// </summary>
    public async Task<T?> GetAsync<T>(
        CorpusCacheKey key,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        if (!File.Exists(path))
        {
            return default;
        }

        FileCorpusCacheEnvelope<T>? envelope;
        try
        {
            await using FileStream stream = File.OpenRead(path);
            envelope = await JsonSerializer
                .DeserializeAsync<FileCorpusCacheEnvelope<T>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            TryDelete(path);
            return default;
        }
        catch (IOException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }

        if (envelope is null || DateTimeOffset.UtcNow - envelope.StoredAt > timeToLive)
        {
            TryDelete(path);
            return default;
        }

        TryTouch(path);
        return envelope.Value;
    }

    /// <summary>
    /// Stores a persisted cache entry and prunes old files.
    /// </summary>
    public async Task SetAsync<T>(
        CorpusCacheKey key,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectory);
        await PruneAsync(1, cancellationToken).ConfigureAwait(false);

        string path = PathFor(key);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        FileCorpusCacheEnvelope<T> envelope = new()
        {
            Key = key,
            StoredAt = DateTimeOffset.UtcNow,
            Value = value
        };
        try
        {
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        await PruneAsync(0, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes old cache files until configured bounds are satisfied.
    /// </summary>
    private async Task PruneAsync(int entriesToReserve, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        List<FileInfo> files = new DirectoryInfo(cacheDirectory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.LastAccessTimeUtc)
            .ToList();
        long totalBytes = files.Sum(file => file.Length);
        int excessEntries = Math.Max(0, files.Count - maxEntries + entriesToReserve);

        foreach (FileInfo file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (excessEntries <= 0 && totalBytes <= maxBytes)
            {
                break;
            }

            totalBytes -= file.Length;
            excessEntries--;
            TryDelete(file.FullName);
            await Task.Yield();
        }
    }

    /// <summary>
    /// Gets the file path for a cache key.
    /// </summary>
    private string PathFor(CorpusCacheKey key)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(key, JsonOptions)));
        return Path.Combine(cacheDirectory, $"{Convert.ToHexString(bytes).ToLowerInvariant()}.json");
    }

    /// <summary>
    /// Deletes a cache file when possible.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Updates cache access time when the filesystem permits it.
    /// </summary>
    private static void TryTouch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Stores a persisted cache value with metadata.
    /// </summary>
    private sealed class FileCorpusCacheEnvelope<T>
    {
        /// <summary>
        /// Gets or sets the original cache key.
        /// </summary>
        public CorpusCacheKey Key { get; set; } = new();

        /// <summary>
        /// Gets or sets when the value was stored.
        /// </summary>
        public DateTimeOffset StoredAt { get; set; }

        /// <summary>
        /// Gets or sets the cached value.
        /// </summary>
        public T? Value { get; set; }
    }
}
