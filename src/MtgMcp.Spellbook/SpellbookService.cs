using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Results;

namespace MtgMcp.Spellbook;

/// <summary>
/// Exposes bounded Commander Spellbook evidence operations through shared typed results.
/// </summary>
public sealed class SpellbookService : IDisposable
{
    /// <summary>
    /// Serializes source deck requests with documented lower-camel-case fields.
    /// </summary>
    private static readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Owns schema validation for the adapter cache lifetime.
    /// </summary>
    private readonly SpellbookDatabase database;

    /// <summary>
    /// Reads and writes exact successful source responses.
    /// </summary>
    private readonly SpellbookCache cache;

    /// <summary>
    /// Reserves source starts and records source cooldowns.
    /// </summary>
    private readonly SpellbookRequestPacer pacer;

    /// <summary>
    /// Sends fixed-origin source requests.
    /// </summary>
    private readonly SpellbookTransport transport;

    /// <summary>
    /// Supplies retrieval and cache-expiration time.
    /// </summary>
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Stores the validated local response freshness duration.
    /// </summary>
    private readonly TimeSpan cacheTtl;

    /// <summary>
    /// Creates the production adapter over one local data root.
    /// </summary>
    public SpellbookService(SpellbookOptions options, string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        timeProvider = TimeProvider.System;
        database = new SpellbookDatabase(options.DataRoot);
        cache = new SpellbookCache(database);
        pacer = new SpellbookRequestPacer(database);
        transport = new SpellbookTransport(packageVersion, timeProvider);
        cacheTtl = options.CacheTtl;
    }

    /// <summary>
    /// Creates a deterministic adapter over injected cache, transport, and time owners.
    /// </summary>
    internal SpellbookService(
        SpellbookOptions options,
        SpellbookDatabase database,
        SpellbookTransport transport,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        cache = new SpellbookCache(database);
        pacer = new SpellbookRequestPacer(database);
        cacheTtl = options.CacheTtl;
    }

    /// <summary>
    /// Searches one bounded source-authoritative variant page.
    /// </summary>
    public Task<OperationResult<SpellbookEvidence>> SearchVariantsAsync(
        SpellbookVariantSearchRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => SearchVariantsCoreAsync(request, cancellationToken));
    }

    /// <summary>
    /// Gets one exact source variant without local interpretation or ranking.
    /// </summary>
    public Task<OperationResult<SpellbookEvidence>> GetVariantAsync(
        string variantId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetVariantCoreAsync(variantId, cancellationToken));
    }

    /// <summary>
    /// Finds source combo groups for an already selected Commander Spellbook deck request.
    /// </summary>
    public Task<OperationResult<SpellbookEvidence>> FindDeckCombosAsync(
        SpellbookDeckComboRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => FindDeckCombosCoreAsync(request, cancellationToken));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        transport.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// Validates and prepares one source variant search without parsing or rewriting its query.
    /// </summary>
    private Task<SpellbookEvidence> SearchVariantsCoreAsync(
        SpellbookVariantSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw Invalid("invalid-variant-search", "request is required.");
        }

        string sourceQuery = SpellbookContract.RequiredSourceQuery(request.SourceQuery, nameof(request.SourceQuery));
        SpellbookPageRequest page = SpellbookContract.Page(request.Page);
        SpellbookRequestDetails details = new(sourceQuery, page, null);
        SpellbookRequestIdentity identity = SpellbookRequestIdentity.Create(
            "variant-search",
            HttpMethod.Get,
            "/variants/",
            details,
            deck: null);
        SpellbookPreparedRequest prepared = new(
            "variant-search",
            HttpMethod.Get,
            $"variants/?{PageQuery(sourceQuery, page)}",
            "GET /variants/",
            Body: null,
            details,
            identity);
        return GetOrAcquireAsync(prepared, cancellationToken);
    }

    /// <summary>
    /// Validates and prepares one exact variant route with a single escaped path segment.
    /// </summary>
    private Task<SpellbookEvidence> GetVariantCoreAsync(string variantId, CancellationToken cancellationToken)
    {
        string exactVariantId = SpellbookContract.RequiredVariantId(variantId, nameof(variantId));
        string escapedVariantId = Uri.EscapeDataString(exactVariantId);
        SpellbookRequestDetails details = new(null, null, exactVariantId);
        SpellbookRequestIdentity identity = SpellbookRequestIdentity.Create(
            "variant-get",
            HttpMethod.Get,
            $"/variants/{escapedVariantId}/",
            details,
            deck: null);
        SpellbookPreparedRequest prepared = new(
            "variant-get",
            HttpMethod.Get,
            $"variants/{escapedVariantId}/",
            $"GET /variants/{escapedVariantId}/",
            Body: null,
            details,
            identity);
        return GetOrAcquireAsync(prepared, cancellationToken);
    }

    /// <summary>
    /// Validates and prepares one deck request without deriving a source query from local deck facts.
    /// </summary>
    private Task<SpellbookEvidence> FindDeckCombosCoreAsync(
        SpellbookDeckComboRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw Invalid("invalid-deck-combo-request", "request is required.");
        }

        SpellbookDeckRequest deck = SpellbookContract.Deck(request.Deck);
        string? sourceQuery = SpellbookContract.OptionalSourceQuery(request.SourceQuery, nameof(request.SourceQuery));
        SpellbookPageRequest page = SpellbookContract.Page(request.Page);
        SpellbookRequestDetails details = new(sourceQuery, page, null);
        SpellbookRequestIdentity identity = SpellbookRequestIdentity.Create(
            "find-my-combos",
            HttpMethod.Post,
            "/find-my-combos",
            details,
            deck);
        SpellbookPreparedRequest prepared = new(
            "find-my-combos",
            HttpMethod.Post,
            $"find-my-combos?{PageQuery(sourceQuery, page)}",
            "POST /find-my-combos",
            JsonSerializer.Serialize(deck, serializerOptions),
            details,
            identity);
        return GetOrAcquireAsync(prepared, cancellationToken);
    }

    /// <summary>
    /// Returns a fresh cache entry or sends exactly one paced source request and stores a complete object result.
    /// </summary>
    private async Task<SpellbookEvidence> GetOrAcquireAsync(
        SpellbookPreparedRequest request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        SpellbookCachedResponse? cached = await cache.TryGetAsync(
            request.Identity,
            now,
            cacheTtl,
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            if (!string.Equals(SpellbookHash.Compute(cached.Json), cached.SourceChecksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The Commander Spellbook cache response checksum is invalid.");
            }

            return CreateEvidence(
                request,
                cached.SourceApiVersion,
                cached.ContractChecksum,
                cached.RetrievedAtUtc,
                "cached",
                cached.SourceChecksum,
                SpellbookContract.ParseResponseObject(cached.Json));
        }

        TimeSpan delay = await pacer.ReserveStartAsync(now, cancellationToken).ConfigureAwait(false);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        await pacer.ThrowIfCoolingDownAsync(timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        try
        {
            SpellbookNetworkResponse response = await transport.SendAsync(
                request.Method,
                request.PathAndQuery,
                request.Body,
                cancellationToken).ConfigureAwait(false);
            JsonElement data = SpellbookContract.ParseResponseObject(response.Json);
            await cache.StoreAsync(request.Identity, response, cacheTtl, cancellationToken).ConfigureAwait(false);
            return CreateEvidence(
                request,
                SpellbookContract.ApiVersion,
                request.Identity.ContractChecksum,
                response.RetrievedAtUtc,
                "network",
                response.SourceChecksum,
                data);
        }
        catch (SpellbookProviderException exception) when (exception.Kind == SpellbookFailureKind.RateLimited)
        {
            await pacer.RecordCooldownAsync(
                timeProvider.GetUtcNow(),
                exception.RetryAfter,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Builds one source evidence record without adding local interpretation to source JSON.
    /// </summary>
    private static SpellbookEvidence CreateEvidence(
        SpellbookPreparedRequest request,
        string sourceApiVersion,
        string contractChecksum,
        DateTimeOffset retrievedAtUtc,
        string cacheStatus,
        string sourceChecksum,
        JsonElement data)
    {
        return new SpellbookEvidence(
            SpellbookContract.SourceName,
            request.Operation,
            request.Details,
            request.Endpoint,
            sourceApiVersion,
            contractChecksum,
            retrievedAtUtc,
            cacheStatus,
            sourceChecksum,
            SpellbookContract.SourceUrl,
            SpellbookContract.Limitations,
            data);
    }

    /// <summary>
    /// Builds a deterministic escaped source query string with every explicit page control.
    /// </summary>
    private static string PageQuery(string? sourceQuery, SpellbookPageRequest page)
    {
        List<KeyValuePair<string, string>> values = [];
        if (sourceQuery is not null)
        {
            values.Add(new KeyValuePair<string, string>("q", sourceQuery));
        }

        values.Add(new KeyValuePair<string, string>("limit", page.Limit.ToString(CultureInfo.InvariantCulture)));
        values.Add(new KeyValuePair<string, string>("offset", page.Offset.ToString(CultureInfo.InvariantCulture)));
        values.Add(new KeyValuePair<string, string>("groupByCombo", page.GroupByCombo ? "true" : "false"));
        return string.Join("&", values.Select(value =>
            $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"));
    }

    /// <summary>
    /// Converts expected source and input failures into the shared exhaustive operation result union.
    /// </summary>
    private static async Task<OperationResult<SpellbookEvidence>> ExecuteAsync(Func<Task<SpellbookEvidence>> operation)
    {
        try
        {
            return new OperationSuccess<SpellbookEvidence>(await operation().ConfigureAwait(false));
        }
        catch (SpellbookProviderException exception)
        {
            return exception.Kind switch
            {
                SpellbookFailureKind.InvalidInput => new OperationInvalidInput(exception.ReasonCode, exception.Message),
                SpellbookFailureKind.NotFound => new OperationNotFound(exception.ReasonCode, exception.Message),
                SpellbookFailureKind.Unsupported => new OperationUnsupported(exception.ReasonCode, exception.Message),
                SpellbookFailureKind.Unavailable or SpellbookFailureKind.RateLimited => new OperationUnavailable(
                    exception.ReasonCode,
                    exception.Message),
                _ => new OperationUnavailable("source-unavailable", "Commander Spellbook could not satisfy the request."),
            };
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return new OperationUnavailable(
                "spellbook-cache-busy",
                "The Commander Spellbook local cache is busy.");
        }
        catch (Exception exception) when (
            exception is InvalidDataException or SqliteException or IOException or UnauthorizedAccessException)
        {
            return new OperationUnavailable(
                "spellbook-cache-unavailable",
                "The Commander Spellbook local cache is unavailable.");
        }
    }

    /// <summary>
    /// Creates one safe invalid-input failure before source or cache work starts.
    /// </summary>
    private static SpellbookProviderException Invalid(string reasonCode, string message)
    {
        return new SpellbookProviderException(SpellbookFailureKind.InvalidInput, reasonCode, message);
    }
}
