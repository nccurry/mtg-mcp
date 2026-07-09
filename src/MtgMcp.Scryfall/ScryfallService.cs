using System.Globalization;
using System.Text.Json;
using MtgMcp.Core.Evidence;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Owns local-first card, query, metadata, and tag evidence plus their shared runtime resources.
/// </summary>
internal sealed class ScryfallCardEvidenceOperations : IDisposable
{
    /// <summary>
    /// Defines the fixed official corpus profile in deterministic order.
    /// </summary>
    private static readonly string[] CorpusDatasetTypes =
        ["all_cards", "rulings", "oracle_tags", "art_tags"];

    /// <summary>
    /// Pins the documented provider order vocabulary instead of forwarding invented enum values.
    /// </summary>
    private static readonly string[] SearchOrders =
    [
        "name", "set", "released", "rarity", "color", "usd", "tix", "eur", "cmc", "power",
        "toughness", "edhrec", "penny", "artist", "review",
    ];

    /// <summary>
    /// Pins the documented provider catalog endpoints available through this typed surface.
    /// </summary>
    private static readonly string[] CatalogNames =
    [
        "card-names", "artist-names", "word-bank", "supertypes", "card-types", "artifact-types",
        "battle-types", "creature-types", "enchantment-types", "land-types", "planeswalker-types",
        "spell-types", "powers", "toughnesses", "loyalties", "watermarks", "keyword-abilities",
        "keyword-actions", "ability-words", "flavor-words",
    ];

    /// <summary>
    /// Uses web JSON conventions for canonical request fingerprints and collection payloads.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores the unified local persistence boundary.
    /// </summary>
    private readonly ScryfallDatabase database;

    /// <summary>
    /// Owns corpus, card, ruling, and tag persistence operations.
    /// </summary>
    internal ScryfallCorpusStore CorpusStore { get; }

    /// <summary>
    /// Owns immutable request snapshot persistence operations.
    /// </summary>
    internal ScryfallSnapshotStore SnapshotStore { get; }

    /// <summary>
    /// Owns cross-process acquisition leases and provider pacing coordination.
    /// </summary>
    internal ScryfallRequestCoordinationStore CoordinationStore { get; }

    /// <summary>
    /// Stores the bounded official provider boundary.
    /// </summary>
    internal ScryfallProviderClient Provider { get; }

    /// <summary>
    /// Supplies deterministic time and delays.
    /// </summary>
    internal TimeProvider TimeProvider { get; }

    /// <summary>
    /// Selects snapshot eligibility.
    /// </summary>
    internal TimeSpan FreshnessTtl { get; }

    /// <summary>
    /// Records whether this process may mutate the local evidence store.
    /// </summary>
    internal bool AllowLocalWrites { get; }

    /// <summary>
    /// Stores the private data root only for free-space preflight.
    /// </summary>
    internal string DataRoot { get; }

    /// <summary>
    /// Creates the shared Scryfall capability with official production defaults.
    /// </summary>
    internal ScryfallCardEvidenceOperations(
        string dataRoot,
        bool allowLocalWrites,
        string packageVersion,
        Uri? apiBaseUri = null,
        TimeSpan? freshnessTtl = null,
        TimeProvider? timeProvider = null,
        HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        DataRoot = dataRoot;
        AllowLocalWrites = allowLocalWrites;
        FreshnessTtl = freshnessTtl ?? TimeSpan.FromHours(24);
        if (FreshnessTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(freshnessTtl), "Freshness TTL must be positive.");
        }

        TimeProvider = timeProvider ?? System.TimeProvider.System;
        database = new ScryfallDatabase(dataRoot);
        CorpusStore = new ScryfallCorpusStore(database);
        SnapshotStore = new ScryfallSnapshotStore(database);
        CoordinationStore = new ScryfallRequestCoordinationStore(database);
        Provider = new ScryfallProviderClient(
            apiBaseUri ?? new Uri("https://api.scryfall.com/", UriKind.Absolute),
            $"mtg-mcp/{packageVersion}",
            CoordinationStore,
            TimeProvider,
            handler);
    }

    /// <summary>
    /// Executes one provider-authoritative Scryfall search or replays its exact request snapshot.
    /// </summary>
    public async Task<OperationResult<ScryfallSearchResult>> SearchAsync(
        string query,
        string unique = "cards",
        string order = "name",
        string direction = "auto",
        bool includeExtras = false,
        bool includeMultilingual = false,
        bool includeVariations = false,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new OperationInvalidInput("invalid-scryfall-query", "Scryfall search query cannot be blank.");
        }

        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, includeRaw);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        if (!AllowedValue(unique, "cards", "art", "prints") ||
            !AllowedValue(direction, "auto", "asc", "desc") ||
            !SearchOrders.Contains(order, StringComparer.Ordinal))
        {
            return new OperationInvalidInput("invalid-scryfall-search-options", "Scryfall search options are invalid.");
        }

        SortedDictionary<string, object?> request = Request(
            ("direction", direction),
            ("includeExtras", includeExtras),
            ("includeMultilingual", includeMultilingual),
            ("includeVariations", includeVariations),
            ("operation", "search"),
            ("order", order),
            ("query", query),
            ("unique", unique));
        string path =
            $"cards/search?q={Uri.EscapeDataString(query)}&unique={Uri.EscapeDataString(unique)}" +
            $"&order={Uri.EscapeDataString(order)}&dir={Uri.EscapeDataString(direction)}" +
            $"&include_extras={includeExtras.ToString().ToLowerInvariant()}" +
            $"&include_multilingual={includeMultilingual.ToString().ToLowerInvariant()}" +
            $"&include_variations={includeVariations.ToString().ToLowerInvariant()}";
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            "search",
            request,
            freshnessPolicy,
            token => Provider.GetPagedAsync(path, token),
            ValidateCardMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallSearchResult>(acquisition);
        }

        OperationResult<ScryfallPage<ScryfallCard>> page = await CardPageAsync(
            success.Data,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken).ConfigureAwait(false);
        return page switch
        {
            OperationSuccess<ScryfallPage<ScryfallCard>> value =>
                new OperationSuccess<ScryfallSearchResult>(
                    new ScryfallSearchResult(value.Data, SnapshotReference(success.Data), success.Data.Warnings)),
            _ => ForwardFailure<ScryfallPage<ScryfallCard>, ScryfallSearchResult>(page),
        };
    }

    /// <summary>
    /// Resolves one card from the active corpus before using an exact request snapshot or provider read.
    /// </summary>
    public async Task<OperationResult<ScryfallCardResult>> GetCardAsync(
        ScryfallCardLookup lookup,
        string freshnessPolicy = "default",
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? lookupFailure = ValidateLookup(lookup);
        if (lookupFailure is not null)
        {
            return lookupFailure;
        }

        OperationResult<ScryfallFreshnessPolicy> policyResult = ParseFreshness(freshnessPolicy);
        if (policyResult is not OperationSuccess<ScryfallFreshnessPolicy> policy)
        {
            return ForwardFailure<ScryfallFreshnessPolicy, ScryfallCardResult>(policyResult);
        }

        if (policy.Data != ScryfallFreshnessPolicy.Refresh && lookup.Kind != "fuzzy-name")
        {
            StoredCorpusObject? stored = await CorpusStore.FindCardAsync(lookup, cancellationToken).ConfigureAwait(false);
            if (stored is not null)
            {
                ScryfallCard card = await MapCorpusCardAsync(stored, includeRaw, cancellationToken).ConfigureAwait(false);
                return new OperationSuccess<ScryfallCardResult>(
                    new ScryfallCardResult(card, "corpus", null, stored.GenerationId));
            }
        }

        SortedDictionary<string, object?> request = Request(
            ("collectorNumber", lookup.CollectorNumber),
            ("kind", lookup.Kind),
            ("operation", "card-get"),
            ("setCode", lookup.SetCode),
            ("value", lookup.Value));
        string path = BuildCardPath(lookup);
        Func<CancellationToken, Task<ProviderAcquisition>> acquire = lookup.Kind == "oracle-id"
            ? token => Provider.GetPagedAsync(path, token)
            : token => Provider.GetSingleAsync(path, token);
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            "card-get",
            request,
            freshnessPolicy,
            acquire,
            ValidateCardMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallCardResult>(acquisition);
        }

        string? raw = success.Data.Stored.Members.Count > 0 ? success.Data.Stored.Members[0] : null;
        if (raw is null)
        {
            return new OperationNotFound("scryfall-card-not-found", "The requested card was not found.");
        }

        ScryfallCard cardResult = MapSnapshotCard(raw, success.Data.Stored.Header, includeRaw);
        return new OperationSuccess<ScryfallCardResult>(
            new ScryfallCardResult(cardResult, "request-snapshot", SnapshotReference(success.Data), null));
    }

    /// <summary>
    /// Resolves and stably pages an ordered batch of at most 150 card identifiers with local-first partitioning.
    /// </summary>
    public async Task<OperationResult<ScryfallCollectionResult>> GetCollectionAsync(
        IReadOnlyList<ScryfallCardLookup>? lookups,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        if (lookups is null || lookups.Count is < 1 or > 150)
        {
            return new OperationInvalidInput(
                "invalid-scryfall-collection",
                "Scryfall collection lookup requires 1 through 150 identifiers.");
        }

        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, includeRaw);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        OperationResult<ScryfallFreshnessPolicy> policyResult = ParseFreshness(freshnessPolicy);
        if (policyResult is not OperationSuccess<ScryfallFreshnessPolicy> policy)
        {
            return ForwardFailure<ScryfallFreshnessPolicy, ScryfallCollectionResult>(policyResult);
        }

        foreach (ScryfallCardLookup lookup in lookups)
        {
            OperationInvalidInput? invalid = ValidateLookup(lookup);
            if (invalid is not null)
            {
                return invalid;
            }

            if (lookup.Kind == "fuzzy-name")
            {
                return new OperationInvalidInput(
                    "fuzzy-collection-lookup-unsupported",
                    "Scryfall collection lookup does not support fuzzy names; use card get instead.");
            }
        }

        string requestHash = CollectionRequestHash(lookups);
        if (cursor is not null)
        {
            return await ContinueCollectionAsync(
                lookups,
                requestHash,
                cursor,
                pageSize,
                includeRaw,
                cancellationToken).ConfigureAwait(false);
        }

        Guid? corpusGenerationId = policy.Data == ScryfallFreshnessPolicy.Refresh
            ? null
            : await CorpusStore.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false);
        List<StoredCorpusObject?> corpusMatches = [];
        List<(int Index, ScryfallCardLookup Lookup)> misses = [];
        for (int index = 0; index < lookups.Count; index++)
        {
            ScryfallCardLookup lookup = lookups[index];
            StoredCorpusObject? stored = corpusGenerationId is Guid generationId
                ? await CorpusStore.FindCardInGenerationAsync(lookup, generationId, cancellationToken).ConfigureAwait(false)
                : null;
            corpusMatches.Add(stored);
            if (stored is null)
            {
                misses.Add((index, lookup));
            }
        }

        AcquiredSnapshot? acquired = null;
        string missStatus = "not-found";
        if (misses.Count > 0)
        {
            List<JsonElement> identifiers = [];
            HashSet<string> identifierKeys = new(StringComparer.Ordinal);
            foreach ((int Index, ScryfallCardLookup Lookup) miss in misses)
            {
                if (identifierKeys.Add(CollectionKey(miss.Lookup)))
                {
                    identifiers.Add(CollectionIdentifier(miss.Lookup));
                }
            }

            SortedDictionary<string, object?> request = Request(
                ("identifiers", identifiers),
                ("operation", "card-collection"),
                ("providerBatchSize", 75));
            OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
                "card-collection",
                request,
                freshnessPolicy,
                token => AcquireCollectionBatchesAsync(identifiers, token),
                ValidateCardMembers,
                cancellationToken).ConfigureAwait(false);
            if (acquisition is OperationSuccess<AcquiredSnapshot> success)
            {
                acquired = success.Data;
            }
            else if (acquisition is OperationNotCached && policy.Data == ScryfallFreshnessPolicy.CacheOnly)
            {
                missStatus = "not-cached";
            }
            else
            {
                return ForwardFailure<AcquiredSnapshot, ScryfallCollectionResult>(acquisition);
            }
        }

        CollectionResolution resolution = await BuildCollectionResolutionAsync(
            lookups,
            corpusMatches,
            acquired?.Stored,
            missStatus,
            includeRaw,
            cancellationToken).ConfigureAwait(false);
        ScryfallCollectionCursorState state = new(
            0,
            requestHash,
            corpusGenerationId,
            acquired?.Stored.Header.SnapshotId,
            acquired?.Stored.Header.Checksum,
            resolution.Checksum,
            missStatus,
            0);
        return CollectionPage(
            resolution.Rows,
            state,
            pageSize,
            acquired is null ? null : SnapshotReference(acquired),
            corpusGenerationId);
    }

    /// <summary>
    /// Resolves exact deck-identity evidence without fuzzy matching or arbitrary non-English printing selection.
    /// </summary>
    public async Task<OperationResult<ScryfallExactCollectionEvidence>> ResolveExactCollectionAsync(
        IReadOnlyList<ScryfallEvidenceLookup>? lookups,
        string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? validation = ValidateEvidenceLookups(lookups);
        if (validation is not null)
        {
            return validation;
        }

        OperationResult<ScryfallFreshnessPolicy> policyResult = ParseFreshness(freshnessPolicy);
        if (policyResult is not OperationSuccess<ScryfallFreshnessPolicy> policy)
        {
            return ForwardFailure<ScryfallFreshnessPolicy, ScryfallExactCollectionEvidence>(policyResult);
        }

        IReadOnlyList<ScryfallEvidenceLookup> validatedLookups = lookups!;
        Guid? corpusGenerationId = policy.Data == ScryfallFreshnessPolicy.Refresh
            ? null
            : await CorpusStore.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false);
        List<StoredCorpusObject?> corpusMatches = await LoadExactCorpusMatchesAsync(
            validatedLookups,
            corpusGenerationId,
            cancellationToken).ConfigureAwait(false);
        List<JsonElement> providerIdentifiers = BuildExactProviderIdentifiers(validatedLookups, corpusMatches);
        AcquiredSnapshot? acquired = null;
        if (providerIdentifiers.Count > 0)
        {
            SortedDictionary<string, object?> request = Request(
                ("identifiers", providerIdentifiers),
                ("operation", "card-collection"),
                ("providerBatchSize", 75));
            OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
                "card-collection",
                request,
                freshnessPolicy,
                token => AcquireCollectionBatchesAsync(providerIdentifiers, token),
                ValidateCardMembers,
                cancellationToken).ConfigureAwait(false);
            if (acquisition is OperationSuccess<AcquiredSnapshot> success)
            {
                acquired = success.Data;
            }
            else if (acquisition is not OperationNotCached ||
                     policy.Data != ScryfallFreshnessPolicy.CacheOnly)
            {
                return ForwardFailure<AcquiredSnapshot, ScryfallExactCollectionEvidence>(acquisition);
            }
        }

        CollectionResolution resolution = await BuildExactCollectionResolutionAsync(
            validatedLookups,
            corpusMatches,
            acquired?.Stored,
            includeRaw: false,
            cancellationToken).ConfigureAwait(false);
        ScryfallSnapshotReference? snapshot = acquired is null ? null : SnapshotReference(acquired);
        return new OperationSuccess<ScryfallExactCollectionEvidence>(
            new ScryfallExactCollectionEvidence(
                resolution.Rows,
                new ScryfallCollectionEvidenceBinding(
                    corpusGenerationId,
                    snapshot,
                    resolution.Checksum)));
    }

    /// <summary>
    /// Replays exact deck-identity resolution from one retained evidence binding without provider access or writes.
    /// </summary>
    public async Task<OperationResult<ScryfallExactCollectionEvidence>> ReplayExactCollectionAsync(
        IReadOnlyList<ScryfallEvidenceLookup>? lookups,
        ScryfallCollectionEvidenceBinding? binding,
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? validation = ValidateEvidenceLookups(lookups);
        if (validation is not null || binding is null || string.IsNullOrWhiteSpace(binding.EvidenceChecksum))
        {
            return validation ?? new OperationInvalidInput(
                "invalid-scryfall-evidence-binding",
                "The Scryfall evidence binding is invalid.");
        }

        if (binding.CorpusGenerationId is Guid generationId &&
            !await CorpusStore.ContainsCompleteGenerationAsync(generationId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityEvidenceUnavailable("The required Scryfall corpus generation is no longer retained.");
        }

        StoredSnapshot? snapshot = null;
        if (binding.Snapshot is ScryfallSnapshotReference snapshotReference)
        {
            snapshot = await SnapshotStore.FindByIdAsync(snapshotReference.SnapshotId, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                return IdentityEvidenceUnavailable("The required Scryfall request snapshot is no longer retained.");
            }

            if (!string.Equals(snapshot.Header.Operation, "card-collection", StringComparison.Ordinal) ||
                !string.Equals(snapshot.Header.Checksum, snapshotReference.Checksum, StringComparison.Ordinal))
            {
                return new OperationInvalidInput(
                    "invalid-scryfall-evidence-binding",
                    "The Scryfall request snapshot does not match the evidence binding.");
            }
        }

        IReadOnlyList<ScryfallEvidenceLookup> validatedLookups = lookups!;
        List<StoredCorpusObject?> corpusMatches = await LoadExactCorpusMatchesAsync(
            validatedLookups,
            binding.CorpusGenerationId,
            cancellationToken).ConfigureAwait(false);
        CollectionResolution resolution = await BuildExactCollectionResolutionAsync(
            validatedLookups,
            corpusMatches,
            snapshot,
            includeRaw: false,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(resolution.Checksum, binding.EvidenceChecksum, StringComparison.Ordinal))
        {
            return IdentityEvidenceUnavailable("The retained Scryfall evidence no longer reproduces the preview.");
        }

        return new OperationSuccess<ScryfallExactCollectionEvidence>(
            new ScryfallExactCollectionEvidence(resolution.Rows, binding));
    }

    /// <summary>
    /// Validates the bounded exact evidence vocabulary before storage or provider access.
    /// </summary>
    private static OperationInvalidInput? ValidateEvidenceLookups(
        IReadOnlyList<ScryfallEvidenceLookup>? lookups)
    {
        if (lookups is null || lookups.Count is < 1 or > 150)
        {
            return new OperationInvalidInput(
                "invalid-scryfall-collection",
                "Scryfall exact evidence resolution requires 1 through 150 identifiers.");
        }

        foreach (ScryfallEvidenceLookup evidenceLookup in lookups)
        {
            if (evidenceLookup is null)
            {
                return new OperationInvalidInput(
                    "invalid-scryfall-lookup",
                    "Scryfall lookup descriptors cannot be null.");
            }

            OperationInvalidInput? invalid = ValidateLookup(evidenceLookup.Lookup);
            if (invalid is not null)
            {
                return invalid;
            }

            if (string.Equals(evidenceLookup.Lookup.Kind, "fuzzy-name", StringComparison.Ordinal) ||
                evidenceLookup.RequiredLanguage is not null &&
                (string.IsNullOrWhiteSpace(evidenceLookup.RequiredLanguage) ||
                 !string.Equals(
                     evidenceLookup.RequiredLanguage,
                     evidenceLookup.RequiredLanguage.Trim().ToLowerInvariant(),
                     StringComparison.Ordinal)))
            {
                return new OperationInvalidInput(
                    "invalid-scryfall-lookup",
                    "Exact evidence lookups require non-fuzzy identities and normalized language codes.");
            }
        }

        return null;
    }

    /// <summary>
    /// Loads exact candidates from one retained generation, including printing language when supplied.
    /// </summary>
    private async Task<List<StoredCorpusObject?>> LoadExactCorpusMatchesAsync(
        IReadOnlyList<ScryfallEvidenceLookup> lookups,
        Guid? corpusGenerationId,
        CancellationToken cancellationToken)
    {
        List<StoredCorpusObject?> matches = [];
        foreach (ScryfallEvidenceLookup lookup in lookups)
        {
            StoredCorpusObject? stored = corpusGenerationId is Guid generationId
                ? await CorpusStore.FindCardInGenerationAsync(
                    lookup.Lookup,
                    generationId,
                    lookup.RequiredLanguage,
                    cancellationToken).ConfigureAwait(false)
                : null;
            matches.Add(stored);
        }

        return matches;
    }

    /// <summary>
    /// Builds globally deduplicated provider identifiers for misses the collection endpoint can represent exactly.
    /// </summary>
    private static List<JsonElement> BuildExactProviderIdentifiers(
        IReadOnlyList<ScryfallEvidenceLookup> lookups,
        IReadOnlyList<StoredCorpusObject?> corpusMatches)
    {
        List<JsonElement> identifiers = [];
        HashSet<string> keys = new(StringComparer.Ordinal);
        for (int index = 0; index < lookups.Count; index++)
        {
            ScryfallEvidenceLookup evidenceLookup = lookups[index];
            if (corpusMatches[index] is not null ||
                evidenceLookup.RequiredLanguage is not null && evidenceLookup.RequiredLanguage != "en")
            {
                continue;
            }

            if (keys.Add(CollectionKey(evidenceLookup.Lookup)))
            {
                identifiers.Add(CollectionIdentifier(evidenceLookup.Lookup));
            }
        }

        return identifiers;
    }

    /// <summary>
    /// Reconstructs exact-language result rows and a checksum tied to raw retained evidence.
    /// </summary>
    private async Task<CollectionResolution> BuildExactCollectionResolutionAsync(
        IReadOnlyList<ScryfallEvidenceLookup> lookups,
        IReadOnlyList<StoredCorpusObject?> corpusMatches,
        StoredSnapshot? snapshot,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        List<(string Raw, ScryfallCard Card)> providerCards = [];
        if (snapshot is not null)
        {
            foreach (string raw in snapshot.Members)
            {
                providerCards.Add((raw, MapSnapshotCard(raw, snapshot.Header, includeRaw)));
            }
        }

        List<ScryfallCollectionRow> rows = [];
        List<string> checksumMembers = [];
        for (int index = 0; index < lookups.Count; index++)
        {
            ScryfallEvidenceLookup evidenceLookup = lookups[index];
            StoredCorpusObject? stored = corpusMatches[index];
            string languageKey = evidenceLookup.RequiredLanguage ?? "-";
            if (stored is not null)
            {
                ScryfallCard card = await MapCorpusCardAsync(stored, includeRaw, cancellationToken).ConfigureAwait(false);
                rows.Add(new ScryfallCollectionRow(index, evidenceLookup.Lookup, "found", "corpus", card, null));
                checksumMembers.Add(
                    $"{index}|{CollectionKey(evidenceLookup.Lookup)}|{languageKey}|corpus|" +
                    $"{stored.GenerationId:D}|{ScryfallDatabase.Hash(stored.RawJson)}");
                continue;
            }

            (string Raw, ScryfallCard Card)? providerMatch = providerCards.FirstOrDefault(value =>
                LookupMatches(evidenceLookup.Lookup, value.Card) &&
                (evidenceLookup.RequiredLanguage is null ||
                 string.Equals(evidenceLookup.RequiredLanguage, value.Card.Language, StringComparison.Ordinal)));
            if (providerMatch is { } found && found.Card is not null)
            {
                rows.Add(new ScryfallCollectionRow(
                    index,
                    evidenceLookup.Lookup,
                    "found",
                    "request-snapshot",
                    found.Card,
                    null));
                checksumMembers.Add(
                    $"{index}|{CollectionKey(evidenceLookup.Lookup)}|{languageKey}|request-snapshot|" +
                    $"{snapshot!.Header.SnapshotId:D}|{ScryfallDatabase.Hash(found.Raw)}");
                continue;
            }

            bool providerRepresentable = evidenceLookup.RequiredLanguage is null ||
                evidenceLookup.RequiredLanguage == "en";
            string status = providerRepresentable && snapshot is not null ? "not-found" : "not-cached";
            string message = status == "not-cached"
                ? "Exact card evidence is not cached."
                : "Card was not found by Scryfall.";
            rows.Add(new ScryfallCollectionRow(index, evidenceLookup.Lookup, status, null, null, message));
            checksumMembers.Add($"{index}|{CollectionKey(evidenceLookup.Lookup)}|{languageKey}|{status}");
        }

        return new CollectionResolution(rows, ScryfallDatabase.Hash(string.Join('\n', checksumMembers)));
    }

    /// <summary>
    /// Creates the stable unavailable result used when exact preview evidence was pruned or changed.
    /// </summary>
    private static OperationUnavailable IdentityEvidenceUnavailable(string message)
    {
        return new OperationUnavailable("identity-evidence-unavailable", message);
    }

    /// <summary>
    /// Replays one collection continuation from its exact retained corpus and provider evidence.
    /// </summary>
    private async Task<OperationResult<ScryfallCollectionResult>> ContinueCollectionAsync(
        IReadOnlyList<ScryfallCardLookup> lookups,
        string requestHash,
        string cursor,
        int pageSize,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        if (!ScryfallCursor.TryDecodeCollection(cursor, requestHash, out ScryfallCollectionCursorState? state) ||
            state is null ||
            state.Offset > lookups.Count)
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall collection cursor is invalid for this request.");
        }

        if (state.CorpusGenerationId is Guid generationId &&
            !await CorpusStore.ContainsCompleteGenerationAsync(generationId, cancellationToken).ConfigureAwait(false))
        {
            return new OperationUnavailable(
                "collection-cursor-evidence-unavailable",
                "The corpus generation required by this collection cursor is no longer retained.");
        }

        StoredSnapshot? snapshot = null;
        if (state.SnapshotId is Guid snapshotId)
        {
            snapshot = await SnapshotStore.FindByIdAsync(snapshotId, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new OperationUnavailable(
                    "collection-cursor-evidence-unavailable",
                    "The provider snapshot required by this collection cursor is no longer retained.");
            }

            if (!string.Equals(snapshot.Header.Operation, "card-collection", StringComparison.Ordinal) ||
                !string.Equals(snapshot.Header.Checksum, state.SnapshotChecksum, StringComparison.Ordinal))
            {
                return new OperationInvalidInput("invalid-cursor", "The Scryfall collection cursor evidence does not match.");
            }
        }

        List<StoredCorpusObject?> corpusMatches = await LoadCollectionCorpusMatchesAsync(
            lookups,
            state.CorpusGenerationId,
            cancellationToken).ConfigureAwait(false);
        CollectionResolution resolution = await BuildCollectionResolutionAsync(
            lookups,
            corpusMatches,
            snapshot,
            state.MissStatus,
            includeRaw,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(resolution.Checksum, state.ResultChecksum, StringComparison.Ordinal))
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall collection cursor result does not match.");
        }

        ScryfallSnapshotReference? snapshotReference = snapshot is null
            ? null
            : SnapshotReference(new AcquiredSnapshot(
                snapshot,
                IsStale(snapshot.Header.RetrievedAtUtc) ? "stale" : "fresh",
                []));
        return CollectionPage(
            resolution.Rows,
            state,
            pageSize,
            snapshotReference,
            state.CorpusGenerationId);
    }

    /// <summary>
    /// Executes the provider collection contract in deterministic batches without publishing partial evidence.
    /// </summary>
    private async Task<ProviderAcquisition> AcquireCollectionBatchesAsync(
        IReadOnlyList<JsonElement> identifiers,
        CancellationToken cancellationToken)
    {
        List<string> pages = [];
        List<string> members = [];
        List<string> warnings = [];
        for (int offset = 0; offset < identifiers.Count; offset += 75)
        {
            JsonElement[] batch = identifiers.Skip(offset).Take(75).ToArray();
            string providerRequest = JsonSerializer.Serialize(new { identifiers = batch }, SerializerOptions);
            ProviderAcquisition acquired = await Provider.PostCollectionAsync(providerRequest, cancellationToken)
                .ConfigureAwait(false);
            pages.AddRange(acquired.Pages);
            members.AddRange(acquired.Members);
            warnings.AddRange(acquired.Warnings);
        }

        return new ProviderAcquisition(pages, members, warnings);
    }

    /// <summary>
    /// Loads collection candidates from one exact retained generation without consulting current state.
    /// </summary>
    private async Task<List<StoredCorpusObject?>> LoadCollectionCorpusMatchesAsync(
        IReadOnlyList<ScryfallCardLookup> lookups,
        Guid? corpusGenerationId,
        CancellationToken cancellationToken)
    {
        List<StoredCorpusObject?> matches = [];
        foreach (ScryfallCardLookup lookup in lookups)
        {
            StoredCorpusObject? stored = corpusGenerationId is Guid generationId
                ? await CorpusStore.FindCardInGenerationAsync(lookup, generationId, cancellationToken).ConfigureAwait(false)
                : null;
            matches.Add(stored);
        }

        return matches;
    }

    /// <summary>
    /// Reconstructs ordered collection rows and a raw-evidence checksum independent of output detail.
    /// </summary>
    private async Task<CollectionResolution> BuildCollectionResolutionAsync(
        IReadOnlyList<ScryfallCardLookup> lookups,
        IReadOnlyList<StoredCorpusObject?> corpusMatches,
        StoredSnapshot? snapshot,
        string missStatus,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        List<(string Raw, ScryfallCard Card)> providerCards = [];
        if (snapshot is not null)
        {
            foreach (string raw in snapshot.Members)
            {
                providerCards.Add((raw, MapSnapshotCard(raw, snapshot.Header, includeRaw)));
            }
        }

        List<ScryfallCollectionRow> rows = [];
        List<string> checksumMembers = [];
        for (int index = 0; index < lookups.Count; index++)
        {
            ScryfallCardLookup lookup = lookups[index];
            StoredCorpusObject? stored = corpusMatches[index];
            if (stored is not null)
            {
                ScryfallCard card = await MapCorpusCardAsync(stored, includeRaw, cancellationToken).ConfigureAwait(false);
                rows.Add(new ScryfallCollectionRow(index, lookup, "found", "corpus", card, null));
                checksumMembers.Add(
                    $"{index}|{CollectionKey(lookup)}|corpus|{stored.GenerationId:D}|{ScryfallDatabase.Hash(stored.RawJson)}");
                continue;
            }

            (string Raw, ScryfallCard Card)? providerMatch = providerCards
                .FirstOrDefault(value => LookupMatches(lookup, value.Card));
            if (providerMatch is { } found && found.Card is not null)
            {
                rows.Add(new ScryfallCollectionRow(index, lookup, "found", "request-snapshot", found.Card, null));
                checksumMembers.Add(
                    $"{index}|{CollectionKey(lookup)}|request-snapshot|{snapshot!.Header.SnapshotId:D}|" +
                    ScryfallDatabase.Hash(found.Raw));
                continue;
            }

            string message = missStatus == "not-cached"
                ? "Card evidence is not cached."
                : "Card was not found by Scryfall.";
            rows.Add(new ScryfallCollectionRow(index, lookup, missStatus, null, null, message));
            checksumMembers.Add($"{index}|{CollectionKey(lookup)}|{missStatus}");
        }

        return new CollectionResolution(rows, ScryfallDatabase.Hash(string.Join('\n', checksumMembers)));
    }

    /// <summary>
    /// Projects one bounded collection page and creates the next evidence-bound cursor when needed.
    /// </summary>
    private static OperationResult<ScryfallCollectionResult> CollectionPage(
        IReadOnlyList<ScryfallCollectionRow> rows,
        ScryfallCollectionCursorState state,
        int pageSize,
        ScryfallSnapshotReference? snapshot,
        Guid? corpusGenerationId)
    {
        if (state.Offset > rows.Count)
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall collection cursor offset is invalid.");
        }

        ScryfallCollectionRow[] items = rows.Skip(state.Offset).Take(pageSize).ToArray();
        string? next = state.Offset + items.Length < rows.Count
            ? ScryfallCursor.EncodeCollection(state with { Offset = state.Offset + items.Length })
            : null;
        return new OperationSuccess<ScryfallCollectionResult>(
            new ScryfallCollectionResult(
                new ScryfallPage<ScryfallCollectionRow>(items, rows.Count, next),
                snapshot,
                corpusGenerationId));
    }

    /// <summary>
    /// Returns every printing for one Oracle identity, preferring the active corpus.
    /// </summary>
    public async Task<OperationResult<ScryfallPrintsResult>> GetPrintsAsync(
        Guid oracleId,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, includeRaw);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        OperationResult<ScryfallFreshnessPolicy> policyResult = ParseFreshness(freshnessPolicy);
        if (policyResult is not OperationSuccess<ScryfallFreshnessPolicy> policy)
        {
            return ForwardFailure<ScryfallFreshnessPolicy, ScryfallPrintsResult>(policyResult);
        }

        if (policy.Data != ScryfallFreshnessPolicy.Refresh)
        {
            StoredCorpusCollection? stored = await CorpusStore.GetPrintsAsync(oracleId, cancellationToken).ConfigureAwait(false);
            if (stored is not null && stored.Items.Count > 0)
            {
                OperationResult<ScryfallPage<ScryfallCard>> corpusPage = MapCorpusCardPage(
                    stored,
                    cursor,
                    pageSize,
                    includeRaw);
                return corpusPage switch
                {
                    OperationSuccess<ScryfallPage<ScryfallCard>> value =>
                        new OperationSuccess<ScryfallPrintsResult>(
                            new ScryfallPrintsResult(value.Data, null, stored.GenerationId)),
                    _ => ForwardFailure<ScryfallPage<ScryfallCard>, ScryfallPrintsResult>(corpusPage),
                };
            }
        }

        SortedDictionary<string, object?> request = Request(("operation", "card-prints"), ("oracleId", oracleId));
        string path = $"cards/search?q=oracleid%3A{oracleId:D}&unique=prints&order=released&dir=asc";
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            "card-prints",
            request,
            freshnessPolicy,
            token => Provider.GetPagedAsync(path, token),
            ValidateCardMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallPrintsResult>(acquisition);
        }

        OperationResult<ScryfallPage<ScryfallCard>> page = await CardPageAsync(
            success.Data,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken)
            .ConfigureAwait(false);
        return page switch
        {
            OperationSuccess<ScryfallPage<ScryfallCard>> value =>
                new OperationSuccess<ScryfallPrintsResult>(
                    new ScryfallPrintsResult(value.Data, SnapshotReference(success.Data), null)),
            _ => ForwardFailure<ScryfallPage<ScryfallCard>, ScryfallPrintsResult>(page),
        };
    }

    /// <summary>
    /// Returns ordered rulings for one Oracle identity, using provider acquisition only from a Scryfall card ID.
    /// </summary>
    public async Task<OperationResult<ScryfallRulingsResult>> GetRulingsAsync(
        Guid oracleId,
        Guid? scryfallCardId = null,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, includeRaw);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        OperationResult<ScryfallFreshnessPolicy> policyResult = ParseFreshness(freshnessPolicy);
        if (policyResult is not OperationSuccess<ScryfallFreshnessPolicy> policy)
        {
            return ForwardFailure<ScryfallFreshnessPolicy, ScryfallRulingsResult>(policyResult);
        }

        if (policy.Data != ScryfallFreshnessPolicy.Refresh)
        {
            StoredCorpusCollection? stored = await CorpusStore.GetRulingsAsync(oracleId, cancellationToken).ConfigureAwait(false);
            if (stored is not null && stored.Items.Count > 0)
            {
                OperationResult<ScryfallPage<ScryfallRuling>> corpusPage = MapCorpusRulingPage(
                    stored,
                    cursor,
                    pageSize,
                    includeRaw);
                return corpusPage switch
                {
                    OperationSuccess<ScryfallPage<ScryfallRuling>> value =>
                        new OperationSuccess<ScryfallRulingsResult>(
                            new ScryfallRulingsResult(value.Data, null, stored.GenerationId)),
                    _ => ForwardFailure<ScryfallPage<ScryfallRuling>, ScryfallRulingsResult>(corpusPage),
                };
            }
        }

        if (scryfallCardId is null)
        {
            return policy.Data == ScryfallFreshnessPolicy.CacheOnly
                ? new OperationNotCached("scryfall-rulings-not-cached", "Rulings are not present in the installed corpus.")
                : new OperationInvalidInput("scryfall-card-id-required", "Provider ruling refresh requires a Scryfall card ID.");
        }

        SortedDictionary<string, object?> request = Request(
            ("operation", "card-rulings"),
            ("oracleId", oracleId),
            ("scryfallCardId", scryfallCardId));
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            "card-rulings",
            request,
            freshnessPolicy,
            token => Provider.GetDataArrayAsync($"cards/{scryfallCardId:D}/rulings", token),
            ValidateRulingMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallRulingsResult>(acquisition);
        }

        OperationResult<ScryfallPage<ScryfallRuling>> page = RulingPage(
            success.Data,
            cursor,
            pageSize,
            includeRaw);
        return page switch
        {
            OperationSuccess<ScryfallPage<ScryfallRuling>> value =>
                new OperationSuccess<ScryfallRulingsResult>(
                    new ScryfallRulingsResult(value.Data, SnapshotReference(success.Data), null)),
            _ => ForwardFailure<ScryfallPage<ScryfallRuling>, ScryfallRulingsResult>(page),
        };
    }

    /// <summary>
    /// Returns the complete set list or one exact provider set.
    /// </summary>
    public async Task<OperationResult<ScryfallSetsResult>> GetSetsAsync(
        string? codeOrId = null,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        if (codeOrId is not null && string.IsNullOrWhiteSpace(codeOrId))
        {
            return new OperationInvalidInput("invalid-scryfall-set", "Set code or ID cannot be blank.");
        }

        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, includeRaw);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        codeOrId = codeOrId?.Trim();
        string operation = codeOrId is null ? "sets" : "set-get";
        SortedDictionary<string, object?> request = Request(("codeOrId", codeOrId), ("operation", operation));
        Func<CancellationToken, Task<ProviderAcquisition>> acquire = codeOrId is null
            ? token => Provider.GetDataArrayAsync("sets", token)
            : token => Provider.GetSingleAsync($"sets/{Uri.EscapeDataString(codeOrId)}", token);
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            operation,
            request,
            freshnessPolicy,
            acquire,
            ValidateSetMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallSetsResult>(acquisition);
        }

        OperationResult<ScryfallPage<ScryfallSet>> page = SetPage(success.Data, cursor, pageSize, includeRaw);
        return page switch
        {
            OperationSuccess<ScryfallPage<ScryfallSet>> value =>
                new OperationSuccess<ScryfallSetsResult>(new ScryfallSetsResult(value.Data, SnapshotReference(success.Data))),
            _ => ForwardFailure<ScryfallPage<ScryfallSet>, ScryfallSetsResult>(page),
        };
    }

    /// <summary>
    /// Returns one documented Scryfall catalog with immutable replay.
    /// </summary>
    public async Task<OperationResult<ScryfallCatalogResult>> GetCatalogAsync(
        string catalog,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!CatalogNames.Contains(catalog, StringComparer.Ordinal))
        {
            return new OperationInvalidInput("invalid-scryfall-catalog", "Catalog name is invalid.");
        }

        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, rawSource: false);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        SortedDictionary<string, object?> request = Request(("catalog", catalog), ("operation", "catalog"));
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            "catalog",
            request,
            freshnessPolicy,
            token => Provider.GetDataArrayAsync($"catalog/{Uri.EscapeDataString(catalog)}", token),
            ValidateStringMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallCatalogResult>(acquisition);
        }

        OperationResult<ScryfallPage<string>> page = StringPage(success.Data, cursor, pageSize);
        return page switch
        {
            OperationSuccess<ScryfallPage<string>> value =>
                new OperationSuccess<ScryfallCatalogResult>(
                    new ScryfallCatalogResult(catalog, value.Data, SnapshotReference(success.Data))),
            _ => ForwardFailure<ScryfallPage<string>, ScryfallCatalogResult>(page),
        };
    }

    /// <summary>
    /// Returns provider autocomplete suggestions with immutable replay.
    /// </summary>
    public async Task<OperationResult<ScryfallAutocompleteResult>> AutocompleteAsync(
        string query,
        bool includeExtras = false,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new OperationInvalidInput("invalid-scryfall-autocomplete", "Autocomplete query cannot be blank.");
        }

        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, rawSource: false);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        SortedDictionary<string, object?> request = Request(
            ("includeExtras", includeExtras),
            ("operation", "autocomplete"),
            ("query", query));
        string path =
            $"cards/autocomplete?q={Uri.EscapeDataString(query)}" +
            $"&include_extras={includeExtras.ToString().ToLowerInvariant()}";
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            "autocomplete",
            request,
            freshnessPolicy,
            token => Provider.GetDataArrayAsync(path, token),
            ValidateStringMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallAutocompleteResult>(acquisition);
        }

        OperationResult<ScryfallPage<string>> page = StringPage(success.Data, cursor, pageSize);
        return page switch
        {
            OperationSuccess<ScryfallPage<string>> value =>
                new OperationSuccess<ScryfallAutocompleteResult>(
                    new ScryfallAutocompleteResult(value.Data, SnapshotReference(success.Data))),
            _ => ForwardFailure<ScryfallPage<string>, ScryfallAutocompleteResult>(page),
        };
    }

    /// <summary>
    /// Returns the fixed official bulk metadata profile with immutable replay.
    /// </summary>
    public async Task<OperationResult<ScryfallBulkMetadataResult>> GetBulkMetadataAsync(
        string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        SortedDictionary<string, object?> request = Request(("operation", "bulk-metadata"));
        OperationResult<AcquiredSnapshot> acquisition = await AcquireAsync(
            "bulk-metadata",
            request,
            freshnessPolicy,
            token => Provider.GetDataArrayAsync("bulk-data", token),
            ValidateBulkMembers,
            cancellationToken).ConfigureAwait(false);
        if (acquisition is not OperationSuccess<AcquiredSnapshot> success)
        {
            return ForwardFailure<AcquiredSnapshot, ScryfallBulkMetadataResult>(acquisition);
        }

        List<ScryfallBulkData> datasets = [];
        foreach (string raw in success.Data.Stored.Members)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            ScryfallBulkData dataset = ScryfallMapper.BulkData(document.RootElement);
            if (CorpusDatasetTypes.Contains(dataset.Type, StringComparer.Ordinal))
            {
                datasets.Add(dataset);
            }
        }

        datasets.Sort(static (left, right) => Array.IndexOf(CorpusDatasetTypes, left.Type)
            .CompareTo(Array.IndexOf(CorpusDatasetTypes, right.Type)));
        if (datasets.Count != CorpusDatasetTypes.Length)
        {
            return new OperationUnavailable("incomplete-scryfall-bulk-profile", "Scryfall bulk metadata omitted a required corpus dataset.");
        }

        return new OperationSuccess<ScryfallBulkMetadataResult>(
            new ScryfallBulkMetadataResult(datasets, SnapshotReference(success.Data)));
    }

    /// <summary>
    /// Searches installed tag metadata without provider traffic.
    /// </summary>
    public Task<OperationResult<ScryfallPage<ScryfallTag>>> SearchTagsAsync(
        string query,
        string? tagType = null,
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || (tagType is not null && !AllowedValue(tagType, "oracle", "art")))
        {
            return Task.FromResult<OperationResult<ScryfallPage<ScryfallTag>>>(
                new OperationInvalidInput("invalid-scryfall-tag-search", "Tag search input is invalid."));
        }

        OperationInvalidInput? failure = ValidatePageSize(pageSize, includeRaw);
        return failure is null
            ? CorpusStore.SearchTagsAsync(query.Trim(), tagType, includeRaw, cursor, pageSize, cancellationToken)
            : Task.FromResult<OperationResult<ScryfallPage<ScryfallTag>>>(failure);
    }

    /// <summary>
    /// Returns cards assigned to one installed tag with optional descendant evidence.
    /// </summary>
    public async Task<OperationResult<ScryfallCardsByTagResult>> GetCardsByTagAsync(
        string tagIdentity,
        string tagType,
        bool includeDescendants = false,
        string minimumWeight = "weak",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagIdentity) ||
            !AllowedValue(tagType, "oracle", "art") ||
            ScryfallDatabase.WeightRank(minimumWeight) < 0)
        {
            return new OperationInvalidInput("invalid-scryfall-tag-selector", "Tag selector is invalid.");
        }

        OperationInvalidInput? pageFailure = ValidatePageSize(pageSize, includeRaw);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        OperationResult<StoredCardsByTag> result = await CorpusStore.GetCardsByTagAsync(
            tagIdentity.Trim(),
            tagType,
            includeDescendants,
            minimumWeight,
            cancellationToken).ConfigureAwait(false);
        if (result is not OperationSuccess<StoredCardsByTag> success)
        {
            return ForwardFailure<StoredCardsByTag, ScryfallCardsByTagResult>(result);
        }

        using JsonDocument tagDocument = JsonDocument.Parse(success.Data.TagJson);
        ScryfallTag tag = ScryfallMapper.Tag(tagDocument.RootElement, success.Data.GenerationId, includeRaw);
        string scope = $"tag-cards:{success.Data.GenerationId:D}:{tag.Id:D}:{includeDescendants}:{minimumWeight}";
        string checksum = ScryfallDatabase.Hash(string.Join('|', success.Data.Assignments.Select(value => value.CardJson)));
        if (!ScryfallCursor.TryDecode(cursor, scope, checksum, out int offset))
        {
            return new OperationInvalidInput("invalid-cursor", "The card-by-tag cursor is invalid for this request.");
        }

        List<ScryfallCard> cards = [];
        List<ScryfallTagEvidence> assignments = [];
        foreach (StoredTagAssignment assignment in success.Data.Assignments.Skip(offset).Take(pageSize))
        {
            using JsonDocument cardDocument = JsonDocument.Parse(assignment.CardJson);
            ScryfallTagEvidence evidence = new(
                assignment.TagId,
                assignment.Label,
                assignment.Slug,
                assignment.TagType,
                assignment.Weight,
                assignment.Annotation,
                assignment.Relationship,
                assignment.Path,
                new SourceEvidenceDescriptor(
                    "scryfall-community-tags",
                    success.Data.RetrievedAtUtc,
                    assignment.TagId.ToString("D", CultureInfo.InvariantCulture),
                    success.Data.GenerationId.ToString("D", CultureInfo.InvariantCulture)));
            assignments.Add(evidence);
            cards.Add(ScryfallMapper.Card(
                cardDocument.RootElement,
                success.Data.RetrievedAtUtc,
                success.Data.GenerationId.ToString("D", CultureInfo.InvariantCulture),
                [evidence],
                IsStale(success.Data.ProviderUpdatedAtUtc),
                "selected-tag-only",
                includeRaw));
        }

        string? next = offset + cards.Count < success.Data.Assignments.Count
            ? ScryfallCursor.Encode(scope, checksum, offset + cards.Count)
            : null;
        return new OperationSuccess<ScryfallCardsByTagResult>(
            new ScryfallCardsByTagResult(
                tag,
                new ScryfallPage<ScryfallCard>(cards, success.Data.Assignments.Count, next),
                assignments,
                success.Data.GenerationId));
    }

    /// <summary>
    /// Releases HTTP and local coordination resources.
    /// </summary>
    public void Dispose()
    {
        Provider.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// Acquires or reuses one exact request snapshot under freshness, mode, lease, and pacing rules.
    /// </summary>
    private async Task<OperationResult<AcquiredSnapshot>> AcquireAsync(
        string operation,
        SortedDictionary<string, object?> request,
        string freshnessPolicy,
        Func<CancellationToken, Task<ProviderAcquisition>> acquire,
        Action<IReadOnlyList<string>> validateMembers,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallFreshnessPolicy> policyResult = ParseFreshness(freshnessPolicy);
        if (policyResult is not OperationSuccess<ScryfallFreshnessPolicy> policy)
        {
            return ForwardFailure<ScryfallFreshnessPolicy, AcquiredSnapshot>(policyResult);
        }

        string requestJson = JsonSerializer.Serialize(request, SerializerOptions);
        string fingerprint = ScryfallDatabase.Hash(requestJson);
        DateTimeOffset now = TimeProvider.GetUtcNow();
        if (policy.Data != ScryfallFreshnessPolicy.Refresh)
        {
            DateTimeOffset? minimum = policy.Data == ScryfallFreshnessPolicy.Default ? now - FreshnessTtl : null;
            StoredSnapshot? stored = await SnapshotStore.FindAsync(fingerprint, minimum, cancellationToken)
                .ConfigureAwait(false);
            if (stored is not null)
            {
                string freshness = now - stored.Header.RetrievedAtUtc <= FreshnessTtl ? "fresh" : "stale";
                return new OperationSuccess<AcquiredSnapshot>(new AcquiredSnapshot(stored, freshness, []));
            }
        }

        if (policy.Data == ScryfallFreshnessPolicy.CacheOnly)
        {
            return new OperationNotCached("scryfall-request-not-cached", "The exact Scryfall request is not cached.");
        }

        if (!AllowLocalWrites)
        {
            return LocalWriteRequired<AcquiredSnapshot>();
        }

        string owner = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        StoredSnapshot? staleCandidate = await SnapshotStore.FindAsync(
            fingerprint,
            null,
            cancellationToken).ConfigureAwait(false);
        bool lease = await CoordinationStore.TryAcquireLeaseAsync(
            fingerprint,
            owner,
            now,
            TimeSpan.FromMinutes(10),
            cancellationToken).ConfigureAwait(false);
        if (!lease)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider, cancellationToken).ConfigureAwait(false);
                StoredSnapshot? concurrent = await SnapshotStore.FindAsync(fingerprint, now - FreshnessTtl, cancellationToken)
                    .ConfigureAwait(false);
                if (concurrent is not null && concurrent.Header.SnapshotId != staleCandidate?.Header.SnapshotId)
                {
                    return new OperationSuccess<AcquiredSnapshot>(new AcquiredSnapshot(concurrent, "fresh", []));
                }
            }

            return new OperationUnavailable("scryfall-acquisition-in-progress", "Another process is acquiring this Scryfall request.");
        }

        try
        {
            ProviderAcquisition providerResult = await acquire(cancellationToken).ConfigureAwait(false);
            validateMembers(providerResult.Members);
            StoredSnapshot snapshot = await SnapshotStore.SaveAsync(
                operation,
                requestJson,
                fingerprint,
                providerResult.Pages,
                providerResult.Members,
                TimeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return new OperationSuccess<AcquiredSnapshot>(
                new AcquiredSnapshot(snapshot, "fresh", providerResult.Warnings));
        }
        catch (ScryfallProviderException exception)
        {
            return exception.ReasonCode switch
            {
                "scryfall-not-found" => new OperationNotFound(exception.ReasonCode, exception.Message),
                "invalid-scryfall-query" => new OperationInvalidInput(exception.ReasonCode, exception.Message),
                _ => new OperationUnavailable(
                    exception.ReasonCode,
                    staleCandidate is null
                        ? exception.Message
                        : $"{exception.Message} Stored snapshot {staleCandidate.Header.SnapshotId:D} remains available through cache-only."),
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
        {
            return new OperationUnavailable("invalid-provider-payload", "Scryfall returned data that could not be safely recorded.");
        }
        finally
        {
            await CoordinationStore.ReleaseLeaseAsync(fingerprint, owner, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Maps one corpus card and its direct community-tag evidence.
    /// </summary>
    private async Task<ScryfallCard> MapCorpusCardAsync(
        StoredCorpusObject stored,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(stored.RawJson);
        Guid? oracleId = ScryfallMapper.OptionalGuid(document.RootElement, "oracle_id");
        List<Guid> illustrationIds = [];
        if (ScryfallMapper.OptionalGuid(document.RootElement, "illustration_id") is Guid illustrationId)
        {
            illustrationIds.Add(illustrationId);
        }

        if (document.RootElement.TryGetProperty("card_faces", out JsonElement faces) &&
            faces.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement face in faces.EnumerateArray())
            {
                if (ScryfallMapper.OptionalGuid(face, "illustration_id") is Guid faceIllustrationId &&
                    !illustrationIds.Contains(faceIllustrationId))
                {
                    illustrationIds.Add(faceIllustrationId);
                }
            }
        }

        IReadOnlyList<ScryfallTagEvidence> tags = await CorpusStore.GetDirectTagsInGenerationAsync(
            stored.GenerationId,
            oracleId,
            illustrationIds,
            stored.RetrievedAtUtc,
            cancellationToken).ConfigureAwait(false);
        return ScryfallMapper.Card(
            document.RootElement,
            stored.RetrievedAtUtc,
            stored.GenerationId.ToString("D", CultureInfo.InvariantCulture),
            tags,
            IsStale(stored.ProviderUpdatedAtUtc),
            "complete-direct",
            includeRaw);
    }

    /// <summary>
    /// Maps one request-snapshot card.
    /// </summary>
    private ScryfallCard MapSnapshotCard(string raw, StoredSnapshotHeader header, bool includeRaw)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        return ScryfallMapper.Card(
            document.RootElement,
            header.RetrievedAtUtc,
            header.SnapshotId.ToString("D", CultureInfo.InvariantCulture),
            pricesStale: IsStale(header.RetrievedAtUtc),
            includeRaw: includeRaw);
    }

    /// <summary>
    /// Maps one snapshot card page from a checksum-bound cursor.
    /// </summary>
    private Task<OperationResult<ScryfallPage<ScryfallCard>>> CardPageAsync(
        AcquiredSnapshot acquired,
        string? cursor,
        int pageSize,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoredSnapshotHeader header = acquired.Stored.Header;
        string scope = $"snapshot:{header.SnapshotId:D}";
        if (!ScryfallCursor.TryDecode(cursor, scope, header.Checksum, out int offset) || offset > header.TotalCount)
        {
            return Task.FromResult<OperationResult<ScryfallPage<ScryfallCard>>>(
                new OperationInvalidInput("invalid-cursor", "The Scryfall result cursor is invalid for this request."));
        }

        List<ScryfallCard> items = [];
        foreach (string raw in acquired.Stored.Members.Skip(offset).Take(pageSize))
        {
            items.Add(MapSnapshotCard(raw, header, includeRaw));
        }

        string? next = offset + items.Count < header.TotalCount
            ? ScryfallCursor.Encode(scope, header.Checksum, offset + items.Count)
            : null;
        return Task.FromResult<OperationResult<ScryfallPage<ScryfallCard>>>(
            new OperationSuccess<ScryfallPage<ScryfallCard>>(new ScryfallPage<ScryfallCard>(items, header.TotalCount, next)));
    }

    /// <summary>
    /// Maps a corpus printing page and its generation-bound cursor.
    /// </summary>
    private OperationResult<ScryfallPage<ScryfallCard>> MapCorpusCardPage(
        StoredCorpusCollection stored,
        string? cursor,
        int pageSize,
        bool includeRaw)
    {
        string scope = $"corpus-cards:{stored.GenerationId:D}";
        string checksum = ScryfallDatabase.Hash(string.Join('|', stored.Items));
        if (!ScryfallCursor.TryDecode(cursor, scope, checksum, out int offset))
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall corpus cursor is invalid.");
        }

        List<ScryfallCard> cards = [];
        foreach (string raw in stored.Items.Skip(offset).Take(pageSize))
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            cards.Add(ScryfallMapper.Card(
                document.RootElement,
                stored.RetrievedAtUtc,
                stored.GenerationId.ToString("D", CultureInfo.InvariantCulture),
                pricesStale: IsStale(stored.ProviderUpdatedAtUtc),
                tagCoverage: "not-included",
                includeRaw: includeRaw));
        }

        string? next = offset + cards.Count < stored.Items.Count
            ? ScryfallCursor.Encode(scope, checksum, offset + cards.Count)
            : null;
        return new OperationSuccess<ScryfallPage<ScryfallCard>>(
            new ScryfallPage<ScryfallCard>(cards, stored.Items.Count, next));
    }

    /// <summary>
    /// Maps a corpus ruling page and its generation-bound cursor.
    /// </summary>
    private static OperationResult<ScryfallPage<ScryfallRuling>> MapCorpusRulingPage(
        StoredCorpusCollection stored,
        string? cursor,
        int pageSize,
        bool includeRaw)
    {
        string scope = $"corpus-rulings:{stored.GenerationId:D}";
        string checksum = ScryfallDatabase.Hash(string.Join('|', stored.Items));
        if (!ScryfallCursor.TryDecode(cursor, scope, checksum, out int offset))
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall corpus cursor is invalid.");
        }

        List<ScryfallRuling> rulings = [];
        foreach (string raw in stored.Items.Skip(offset).Take(pageSize))
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            rulings.Add(ScryfallMapper.Ruling(
                document.RootElement,
                stored.RetrievedAtUtc,
                stored.GenerationId.ToString("D", CultureInfo.InvariantCulture),
                includeRaw));
        }

        string? next = offset + rulings.Count < stored.Items.Count
            ? ScryfallCursor.Encode(scope, checksum, offset + rulings.Count)
            : null;
        return new OperationSuccess<ScryfallPage<ScryfallRuling>>(
            new ScryfallPage<ScryfallRuling>(rulings, stored.Items.Count, next));
    }

    /// <summary>
    /// Maps one request-snapshot ruling page.
    /// </summary>
    private static OperationResult<ScryfallPage<ScryfallRuling>> RulingPage(
        AcquiredSnapshot acquired,
        string? cursor,
        int pageSize,
        bool includeRaw)
    {
        StoredSnapshotHeader header = acquired.Stored.Header;
        string scope = $"snapshot:{header.SnapshotId:D}";
        if (!ScryfallCursor.TryDecode(cursor, scope, header.Checksum, out int offset) || offset > header.TotalCount)
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall result cursor is invalid for this request.");
        }

        List<ScryfallRuling> items = [];
        foreach (string raw in acquired.Stored.Members.Skip(offset).Take(pageSize))
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            items.Add(ScryfallMapper.Ruling(
                document.RootElement,
                header.RetrievedAtUtc,
                header.SnapshotId.ToString("D"),
                includeRaw));
        }

        string? next = offset + items.Count < header.TotalCount
            ? ScryfallCursor.Encode(scope, header.Checksum, offset + items.Count)
            : null;
        return new OperationSuccess<ScryfallPage<ScryfallRuling>>(
            new ScryfallPage<ScryfallRuling>(items, header.TotalCount, next));
    }

    /// <summary>
    /// Maps one request-snapshot set page.
    /// </summary>
    private static OperationResult<ScryfallPage<ScryfallSet>> SetPage(
        AcquiredSnapshot acquired,
        string? cursor,
        int pageSize,
        bool includeRaw)
    {
        StoredSnapshotHeader header = acquired.Stored.Header;
        string scope = $"snapshot:{header.SnapshotId:D}";
        if (!ScryfallCursor.TryDecode(cursor, scope, header.Checksum, out int offset) || offset > header.TotalCount)
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall result cursor is invalid for this request.");
        }

        List<ScryfallSet> items = [];
        foreach (string raw in acquired.Stored.Members.Skip(offset).Take(pageSize))
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            items.Add(ScryfallMapper.Set(
                document.RootElement,
                header.RetrievedAtUtc,
                header.SnapshotId.ToString("D"),
                includeRaw));
        }

        string? next = offset + items.Count < header.TotalCount
            ? ScryfallCursor.Encode(scope, header.Checksum, offset + items.Count)
            : null;
        return new OperationSuccess<ScryfallPage<ScryfallSet>>(
            new ScryfallPage<ScryfallSet>(items, header.TotalCount, next));
    }

    /// <summary>
    /// Maps one request-snapshot string page.
    /// </summary>
    private static OperationResult<ScryfallPage<string>> StringPage(
        AcquiredSnapshot acquired,
        string? cursor,
        int pageSize)
    {
        StoredSnapshotHeader header = acquired.Stored.Header;
        string scope = $"snapshot:{header.SnapshotId:D}";
        if (!ScryfallCursor.TryDecode(cursor, scope, header.Checksum, out int offset) || offset > header.TotalCount)
        {
            return new OperationInvalidInput("invalid-cursor", "The Scryfall result cursor is invalid for this request.");
        }

        List<string> items = [];
        foreach (string raw in acquired.Stored.Members.Skip(offset).Take(pageSize))
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.String && document.RootElement.GetString() is string value)
            {
                items.Add(value);
            }
        }

        string? next = offset + items.Count < header.TotalCount
            ? ScryfallCursor.Encode(scope, header.Checksum, offset + items.Count)
            : null;
        return new OperationSuccess<ScryfallPage<string>>(
            new ScryfallPage<string>(items, header.TotalCount, next));
    }

    /// <summary>
    /// Formats the public snapshot reference for a stored acquisition.
    /// </summary>
    private static ScryfallSnapshotReference SnapshotReference(AcquiredSnapshot acquired)
    {
        StoredSnapshotHeader header = acquired.Stored.Header;
        return new ScryfallSnapshotReference(
            header.SnapshotId,
            header.Checksum,
            header.RetrievedAtUtc,
            acquired.Freshness,
            header.PredecessorId);
    }

    /// <summary>
    /// Validates every acquired card before any completed snapshot is published.
    /// </summary>
    private static void ValidateCardMembers(IReadOnlyList<string> members)
    {
        foreach (string raw in members)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            _ = ScryfallMapper.Card(document.RootElement, DateTimeOffset.UnixEpoch, "validation");
        }
    }

    /// <summary>
    /// Validates every acquired ruling before any completed snapshot is published.
    /// </summary>
    private static void ValidateRulingMembers(IReadOnlyList<string> members)
    {
        foreach (string raw in members)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            _ = ScryfallMapper.Ruling(document.RootElement, DateTimeOffset.UnixEpoch, "validation");
        }
    }

    /// <summary>
    /// Validates every acquired set before any completed snapshot is published.
    /// </summary>
    private static void ValidateSetMembers(IReadOnlyList<string> members)
    {
        foreach (string raw in members)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            _ = ScryfallMapper.Set(document.RootElement, DateTimeOffset.UnixEpoch, "validation");
        }
    }

    /// <summary>
    /// Validates catalog and autocomplete membership as strings before snapshot publication.
    /// </summary>
    private static void ValidateStringMembers(IReadOnlyList<string> members)
    {
        foreach (string raw in members)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Scryfall returned a non-string catalog member.");
            }
        }
    }

    /// <summary>
    /// Validates that one metadata response contains each fixed corpus dataset exactly once.
    /// </summary>
    private static void ValidateBulkMembers(IReadOnlyList<string> members)
    {
        HashSet<string> fixedTypes = new(StringComparer.Ordinal);
        foreach (string raw in members)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            string type = ScryfallMapper.RequiredString(document.RootElement, "type");
            if (!CorpusDatasetTypes.Contains(type, StringComparer.Ordinal))
            {
                continue;
            }

            _ = ScryfallMapper.BulkData(document.RootElement);
            if (!fixedTypes.Add(type))
            {
                throw new InvalidDataException("Scryfall returned duplicate fixed bulk metadata.");
            }
        }

        if (fixedTypes.Count != CorpusDatasetTypes.Length)
        {
            throw new InvalidDataException("Scryfall did not return the complete fixed bulk metadata profile.");
        }
    }

    /// <summary>
    /// Parses the exact freshness policy vocabulary.
    /// </summary>
    private static OperationResult<ScryfallFreshnessPolicy> ParseFreshness(string value)
    {
        return value switch
        {
            "default" => new OperationSuccess<ScryfallFreshnessPolicy>(ScryfallFreshnessPolicy.Default),
            "cache-only" => new OperationSuccess<ScryfallFreshnessPolicy>(ScryfallFreshnessPolicy.CacheOnly),
            "refresh" => new OperationSuccess<ScryfallFreshnessPolicy>(ScryfallFreshnessPolicy.Refresh),
            _ => new OperationInvalidInput(
                "invalid-freshness-policy",
                "Freshness policy must be default, cache-only, or refresh."),
        };
    }

    /// <summary>
    /// Validates one card lookup as exactly one supported case.
    /// </summary>
    private static OperationInvalidInput? ValidateLookup(ScryfallCardLookup? lookup)
    {
        if (lookup is null)
        {
            return new OperationInvalidInput("invalid-card-lookup", "Card lookup is required.");
        }

        bool valid = lookup.Kind switch
        {
            "scryfall-id" or "oracle-id" =>
                Guid.TryParse(lookup.Value, out _) && lookup.SetCode is null && lookup.CollectorNumber is null,
            "exact-name" or "fuzzy-name" =>
                !string.IsNullOrWhiteSpace(lookup.Value) && lookup.SetCode is null && lookup.CollectorNumber is null,
            "printing" =>
                lookup.Value is null && !string.IsNullOrWhiteSpace(lookup.SetCode) &&
                !string.IsNullOrWhiteSpace(lookup.CollectorNumber),
            _ => false,
        };
        return valid
            ? null
            : new OperationInvalidInput("invalid-card-lookup", "Card lookup fields do not match one supported lookup case.");
    }

    /// <summary>
    /// Builds one provider card endpoint from an already validated lookup.
    /// </summary>
    private static string BuildCardPath(ScryfallCardLookup lookup)
    {
        return lookup.Kind switch
        {
            "scryfall-id" => $"cards/{lookup.Value}",
            "oracle-id" => $"cards/search?q=oracleid%3A{lookup.Value}&unique=prints&order=released&dir=desc",
            "exact-name" => $"cards/named?exact={Uri.EscapeDataString(lookup.Value!.Trim())}",
            "fuzzy-name" => $"cards/named?fuzzy={Uri.EscapeDataString(lookup.Value!.Trim())}",
            "printing" => $"cards/{Uri.EscapeDataString(lookup.SetCode!.Trim())}/{Uri.EscapeDataString(lookup.CollectorNumber!.Trim())}",
            _ => throw new ArgumentOutOfRangeException(nameof(lookup), "Unknown card lookup case."),
        };
    }

    /// <summary>
    /// Converts one collection lookup into the official identifier object.
    /// </summary>
    private static JsonElement CollectionIdentifier(ScryfallCardLookup lookup)
    {
        object identifier = lookup.Kind switch
        {
            "scryfall-id" => new { id = lookup.Value },
            "oracle-id" => new { oracle_id = lookup.Value },
            "exact-name" or "fuzzy-name" => new { name = lookup.Value!.Trim() },
            "printing" => new { set = lookup.SetCode!.Trim(), collector_number = lookup.CollectorNumber!.Trim() },
            _ => throw new ArgumentOutOfRangeException(nameof(lookup), "Unknown card lookup case."),
        };
        return JsonSerializer.SerializeToElement(identifier, SerializerOptions);
    }

    /// <summary>
    /// Creates a case-normalized identity key for provider collection deduplication.
    /// </summary>
    private static string CollectionKey(ScryfallCardLookup lookup)
    {
        return lookup.Kind switch
        {
            "scryfall-id" or "oracle-id" =>
                $"{lookup.Kind}:{Guid.Parse(lookup.Value!).ToString("D", CultureInfo.InvariantCulture)}",
            "exact-name" => $"name:{lookup.Value!.Trim().ToUpperInvariant()}",
            "printing" => $"printing:{lookup.SetCode!.Trim().ToLowerInvariant()}:{lookup.CollectorNumber!.Trim()}",
            _ => throw new ArgumentOutOfRangeException(nameof(lookup), "Unknown collection lookup case."),
        };
    }

    /// <summary>
    /// Fingerprints the complete ordered semantic lookup list independently of pagination and output detail.
    /// </summary>
    private static string CollectionRequestHash(IReadOnlyList<ScryfallCardLookup> lookups)
    {
        return ScryfallDatabase.Hash(string.Join('\n', lookups.Select(CollectionKey)));
    }

    /// <summary>
    /// Reports whether one returned card satisfies an original collection identifier.
    /// </summary>
    private static bool LookupMatches(ScryfallCardLookup lookup, ScryfallCard card)
    {
        return lookup.Kind switch
        {
            "scryfall-id" => string.Equals(card.Id.ToString("D"), lookup.Value, StringComparison.OrdinalIgnoreCase),
            "oracle-id" => card.OracleId is Guid oracle &&
                string.Equals(oracle.ToString("D"), lookup.Value, StringComparison.OrdinalIgnoreCase),
            "exact-name" or "fuzzy-name" =>
                string.Equals(card.Name, lookup.Value!.Trim(), StringComparison.OrdinalIgnoreCase) ||
                card.Faces.Any(face => string.Equals(face.Name, lookup.Value!.Trim(), StringComparison.OrdinalIgnoreCase)),
            "printing" => string.Equals(card.SetCode, lookup.SetCode!.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(card.CollectorNumber, lookup.CollectorNumber!.Trim(), StringComparison.Ordinal),
            _ => false,
        };
    }

    /// <summary>
    /// Validates bounded output pagination.
    /// </summary>
    private static OperationInvalidInput? ValidatePageSize(int pageSize, bool rawSource)
    {
        int maximum = rawSource ? 25 : 100;
        return pageSize is >= 1 && pageSize <= maximum
            ? null
            : new OperationInvalidInput("invalid-page-size", $"Page size must be from 1 through {maximum}.");
    }

    /// <summary>
    /// Checks one exact string against a closed allowed set.
    /// </summary>
    private static bool AllowedValue(string value, params string[] allowed)
    {
        return allowed.Contains(value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates one canonically key-sorted request object.
    /// </summary>
    private static SortedDictionary<string, object?> Request(params (string Key, object? Value)[] values)
    {
        SortedDictionary<string, object?> request = new(StringComparer.Ordinal)
        {
            ["adapterSchemaVersion"] = 1,
        };
        foreach ((string key, object? value) in values)
        {
            request.Add(key, value);
        }

        return request;
    }

    /// <summary>
    /// Reports whether time-sensitive price and rank evidence exceeded the configured freshness window.
    /// </summary>
    private bool IsStale(DateTimeOffset retrievedAtUtc)
    {
        return TimeProvider.GetUtcNow() - retrievedAtUtc > FreshnessTtl;
    }

    /// <summary>
    /// Returns the uniform read-only-mode failure for a path that would mutate local evidence.
    /// </summary>
    private static OperationResult<T> LocalWriteRequired<T>()
    {
        return new OperationUnavailable(
            "local-write-required",
            "This Scryfall operation requires local mode to record coordinated evidence.");
    }

    /// <summary>
    /// Preserves a structured failure across result payload types.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationSuccess<TSource> => new OperationUnavailable("scryfall-result-mismatch", "Scryfall result could not be projected."),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }
}

/// <summary>
/// Carries one reused or newly persisted provider snapshot and response warnings.
/// </summary>
internal sealed record AcquiredSnapshot(
    StoredSnapshot Stored,
    string Freshness,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Carries one fully reconstructed collection and its immutable evidence checksum before paging.
/// </summary>
internal sealed record CollectionResolution(
    IReadOnlyList<ScryfallCollectionRow> Rows,
    string Checksum);
