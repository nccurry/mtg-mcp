using System.Globalization;
using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Maps observed Archidekt payloads into deterministic provider evidence without leaking transport types.
/// </summary>
internal static class ArchidektJsonContract
{
    /// <summary>
    /// Lists top-level deck fields consumed by the normalized projection.
    /// </summary>
    private static readonly HashSet<string> KnownDeckFields = new(StringComparer.Ordinal)
    {
        "id", "name", "description", "deckFormat", "format", "private", "unlisted",
        "parentFolder", "parentFolderId", "folder", "categories", "cards", "updatedAt",
    };

    /// <summary>
    /// Maps one complete deck payload and fails closed when its identity or cards cannot be understood.
    /// </summary>
    internal static RemoteDeckSnapshot MapDeck(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        JsonElement deck = UnwrapObject(root, "deck", "data");
        string remoteId = RequireId(deck, "id", "Archidekt deck identity is missing.");
        string name = RequireText(deck, "name", "Archidekt deck name is missing.");
        string description = GetString(deck, "description") ?? string.Empty;
        string format = MapFormat(GetString(deck, "deckFormat") ?? GetString(deck, "format"));
        string visibility = MapVisibility(deck);
        string? parentFolderId = GetId(deck, "parentFolderId")
            ?? GetId(deck, "parentFolder")
            ?? GetNestedId(deck, "folder", "id");
        List<RemoteDeckCategory> categories = MapCategories(deck);
        Dictionary<string, RemoteDeckCategory> categoriesById = categories
            .ToDictionary(value => value.ProviderCategoryId, StringComparer.Ordinal);
        Dictionary<string, RemoteDeckCategory> categoriesByName = categories
            .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<RemoteDeckEntry> entries = MapEntries(deck, categoriesById, categoriesByName);
        Dictionary<string, JsonElement> extensions = CopyUnknown(deck, KnownDeckFields);
        string sourceChecksum = ArchidektContract.Hash(sourceJson);
        string contentFingerprint = FingerprintContent(
            name,
            description,
            format,
            visibility,
            parentFolderId,
            categories,
            entries);
        string remoteFingerprint = ArchidektContract.Fingerprint(new
        {
            remoteId,
            contentFingerprint,
            providerCategories = categories.Select(value => value.ProviderCategoryId),
            providerRelations = entries.Select(value => new
            {
                value.ProviderRelationId,
                value.ProviderCardId,
            }),
            extensions,
        });
        ArchidektRetrievalEvidence evidence = Evidence(method, retrievedAtUtc, sourceChecksum);
        return new RemoteDeckSnapshot(
            remoteId,
            $"https://archidekt.com/decks/{remoteId}",
            name,
            description,
            format,
            visibility,
            parentFolderId,
            categories,
            entries,
            extensions,
            evidence,
            contentFingerprint,
            remoteFingerprint);
    }

    /// <summary>
    /// Maps one page of deck summaries while preserving the provider continuation as an opaque cursor.
    /// </summary>
    internal static RemoteDeckPage MapDeckPage(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        List<RemoteDeckSummary> items = [];
        foreach (JsonElement item in EnumerateCollection(root, "decks", "results", "data"))
        {
            items.Add(MapDeckSummary(item));
        }

        items.Sort(static (left, right) =>
        {
            int name = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return name != 0 ? name : StringComparer.Ordinal.Compare(left.RemoteId, right.RemoteId);
        });
        string? next = GetString(root, "next");
        return new RemoteDeckPage(
            items,
            next,
            Evidence(method, retrievedAtUtc, ArchidektContract.Hash(sourceJson)));
    }

    /// <summary>
    /// Maps the recursive folder tree into flat canonical records with explicit parent relationships.
    /// </summary>
    internal static RemoteFolderTree MapFolderTree(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        List<RemoteFolderRecord> items = [];
        if (root.ValueKind == JsonValueKind.Object && GetId(root, "id") is not null)
        {
            MapFolderNode(root, inheritedParentId: null, inheritedPath: null, items);
        }
        else
        {
            foreach (JsonElement item in EnumerateCollection(root, "results", "data", "folders"))
            {
                MapFolderNode(item, inheritedParentId: null, inheritedPath: null, items);
            }
        }

        items.Sort(static (left, right) =>
        {
            int path = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            return path != 0 ? path : StringComparer.Ordinal.Compare(left.FolderId, right.FolderId);
        });
        string sourceChecksum = ArchidektContract.Hash(sourceJson);
        string treeFingerprint = ArchidektContract.Fingerprint(items.Select(FolderProjection));
        return new RemoteFolderTree(
            items,
            Evidence(method, retrievedAtUtc, sourceChecksum),
            treeFingerprint);
    }

    /// <summary>
    /// Maps one folder detail response using the same canonical tree representation.
    /// </summary>
    internal static RemoteFolderTree MapFolderDetail(
        JsonElement root,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        JsonElement folder = UnwrapObject(root, "folder", "data");
        List<RemoteFolderRecord> items = [];
        MapFolderNode(folder, GetId(folder, "parent"), GetString(folder, "path"), items);
        items.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.FolderId, right.FolderId));
        string sourceChecksum = ArchidektContract.Hash(sourceJson);
        return new RemoteFolderTree(
            items,
            Evidence(method, retrievedAtUtc, sourceChecksum),
            ArchidektContract.Fingerprint(items.Select(FolderProjection)));
    }

    /// <summary>
    /// Maps a snapshot collection without pretending that list rows contain complete saved deck state.
    /// </summary>
    internal static RemoteNamedSnapshotPage MapSnapshotPage(
        JsonElement root,
        string deckId,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        List<RemoteNamedSnapshotSummary> items = [];
        foreach (JsonElement item in EnumerateCollection(root, "results", "data", "snapshots"))
        {
            items.Add(MapSnapshotSummary(item, deckId));
        }

        items.Sort(static (left, right) =>
        {
            int created = Nullable.Compare(right.CreatedAtUtc, left.CreatedAtUtc);
            return created != 0 ? created : StringComparer.Ordinal.Compare(left.SnapshotId, right.SnapshotId);
        });
        string checksum = ArchidektContract.Fingerprint(items.Select(value => new
        {
            value.SnapshotId,
            value.DeckId,
            value.Name,
            value.Description,
            value.CreatedAtUtc,
            value.UpdatedAtUtc,
            value.Checksum,
        }));
        return new RemoteNamedSnapshotPage(
            items,
            Evidence(method, retrievedAtUtc, ArchidektContract.Hash(sourceJson)),
            checksum);
    }

    /// <summary>
    /// Maps one complete named snapshot and cross-checks its owning deck identity.
    /// </summary>
    internal static RemoteNamedSnapshot MapSnapshot(
        JsonElement root,
        string expectedDeckId,
        string sourceJson,
        DateTimeOffset retrievedAtUtc,
        string method)
    {
        JsonElement snapshot = UnwrapObject(root, "snapshot", "data");
        RemoteNamedSnapshotSummary summary = MapSnapshotSummary(snapshot, expectedDeckId);
        if (!string.Equals(summary.DeckId, expectedDeckId, StringComparison.Ordinal))
        {
            throw Unsupported("Archidekt snapshot belongs to a different deck.");
        }

        JsonElement deck = snapshot.TryGetProperty("deck", out JsonElement nestedDeck)
            && nestedDeck.ValueKind == JsonValueKind.Object
            ? nestedDeck
            : snapshot;
        RemoteDeckSnapshot savedDeck = MapDeck(
            deck,
            deck.GetRawText(),
            retrievedAtUtc,
            method);
        if (snapshot.TryGetProperty("snapshotMeta", out JsonElement snapshotMeta) &&
            snapshotMeta.ValueKind == JsonValueKind.Object)
        {
            savedDeck = RebuildSnapshotDeck(
                savedDeck,
                GetString(snapshotMeta, "parentDeckName") ?? savedDeck.Name);
        }

        if (!string.Equals(savedDeck.RemoteId, expectedDeckId, StringComparison.Ordinal))
        {
            savedDeck = savedDeck with
            {
                RemoteId = expectedDeckId,
                RemoteUri = $"https://archidekt.com/decks/{expectedDeckId}",
            };
        }

        return new RemoteNamedSnapshot(
            summary,
            savedDeck,
            Evidence(method, retrievedAtUtc, ArchidektContract.Hash(sourceJson)));
    }

    /// <summary>
    /// Maps one deck summary from either a direct list row or folder-contained row.
    /// </summary>
    private static RemoteDeckSummary MapDeckSummary(JsonElement item)
    {
        string remoteId = RequireId(item, "id", "Archidekt deck summary identity is missing.");
        string name = RequireText(item, "name", "Archidekt deck summary name is missing.");
        string visibility = MapVisibility(item);
        string? parentFolderId = GetNestedId(item, "folder", "id")
            ?? GetId(item, "parentFolderId")
            ?? GetId(item, "parentFolder");
        string? folderName = GetNestedString(item, "folder", "name");
        string? folderPath = GetNestedString(item, "folder", "path");
        int? cardCount = GetInt32(item, "cardCount");
        DateTimeOffset? updated = GetDateTime(item, "updatedAt");
        string fingerprint = ArchidektContract.Fingerprint(new
        {
            remoteId,
            name,
            description = GetString(item, "description"),
            format = MapFormat(GetString(item, "deckFormat") ?? GetString(item, "format")),
            visibility,
            parentFolderId,
            folderName,
            folderPath,
            cardCount,
            updated,
        });
        return new RemoteDeckSummary(
            remoteId,
            name,
            GetString(item, "description"),
            MapFormat(GetString(item, "deckFormat") ?? GetString(item, "format")),
            visibility,
            parentFolderId,
            folderName,
            folderPath,
            cardCount,
            updated,
            fingerprint);
    }

    /// <summary>
    /// Maps categories while retaining provider identity and board-inclusion flags.
    /// </summary>
    private static List<RemoteDeckCategory> MapCategories(JsonElement deck)
    {
        List<RemoteDeckCategory> categories = [];
        if (!deck.TryGetProperty("categories", out JsonElement collection) ||
            collection.ValueKind != JsonValueKind.Array)
        {
            return categories;
        }

        int index = 0;
        foreach (JsonElement item in collection.EnumerateArray())
        {
            string id = GetId(item, "id") ?? $"name:{GetString(item, "name") ?? index.ToString(CultureInfo.InvariantCulture)}";
            string name = RequireText(item, "name", "Archidekt category name is missing.");
            categories.Add(new RemoteDeckCategory(
                id,
                name,
                GetBoolean(item, "includedInDeck"),
                GetBoolean(item, "includedInPrice"),
                GetBoolean(item, "isPremier") ?? false,
                GetInt32(item, "sortOrder") ?? index));
            index++;
        }

        categories.Sort(static (left, right) =>
        {
            int sort = left.SortOrder.CompareTo(right.SortOrder);
            if (sort != 0)
            {
                return sort;
            }

            int name = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return name != 0
                ? name
                : StringComparer.Ordinal.Compare(left.ProviderCategoryId, right.ProviderCategoryId);
        });
        return categories;
    }

    /// <summary>
    /// Maps independently addressable card relations with explicit printing and category evidence.
    /// </summary>
    private static List<RemoteDeckEntry> MapEntries(
        JsonElement deck,
        IReadOnlyDictionary<string, RemoteDeckCategory> categoriesById,
        IReadOnlyDictionary<string, RemoteDeckCategory> categoriesByName)
    {
        List<RemoteDeckEntry> entries = [];
        if (!deck.TryGetProperty("cards", out JsonElement collection) ||
            collection.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        int index = 0;
        foreach (JsonElement relation in collection.EnumerateArray())
        {
            JsonElement card = relation.TryGetProperty("card", out JsonElement nestedCard)
                && nestedCard.ValueKind == JsonValueKind.Object
                ? nestedCard
                : throw Unsupported("Archidekt card relation is missing card data.");
            JsonElement oracle = card.TryGetProperty("oracleCard", out JsonElement nestedOracle)
                && nestedOracle.ValueKind == JsonValueKind.Object
                ? nestedOracle
                : throw Unsupported("Archidekt card relation is missing oracle identity.");
            string relationId = GetId(relation, "deckRelationId")
                ?? GetId(relation, "id")
                ?? throw Unsupported("Archidekt card relation identity is missing.");
            string providerCardId = RequireId(card, "id", "Archidekt printing identity is missing.");
            string cardName = RequireText(oracle, "name", "Archidekt card name is missing.");
            List<string> categoryNames = MapCategoryNames(
                relation,
                categoriesById,
                categoriesByName);
            string? primary = categoryNames.FirstOrDefault();
            string zone = MapZone(categoryNames, categoriesByName);
            entries.Add(new RemoteDeckEntry(
                relationId,
                providerCardId,
                Math.Max(1, GetInt32(relation, "quantity") ?? 1),
                cardName,
                ParseGuid(GetString(oracle, "uid")),
                ParseGuid(GetString(card, "uid")),
                GetString(card, "setCode") ?? GetNestedString(card, "edition", "editioncode"),
                GetString(card, "collectorNumber"),
                GetString(relation, "language") ?? GetString(card, "language") ?? "en",
                MapFinish(GetString(relation, "modifier")),
                zone,
                categoryNames,
                primary,
                GetInt32(relation, "order") ?? index));
            index++;
        }

        entries.Sort(static (left, right) =>
        {
            int sort = left.SortOrder.CompareTo(right.SortOrder);
            if (sort != 0)
            {
                return sort;
            }

            int name = StringComparer.OrdinalIgnoreCase.Compare(left.CardName, right.CardName);
            return name != 0
                ? name
                : StringComparer.Ordinal.Compare(left.ProviderRelationId, right.ProviderRelationId);
        });
        return entries;
    }

    /// <summary>
    /// Resolves mixed provider category IDs, names, and objects without inventing missing categories.
    /// </summary>
    private static List<string> MapCategoryNames(
        JsonElement relation,
        IReadOnlyDictionary<string, RemoteDeckCategory> categoriesById,
        IReadOnlyDictionary<string, RemoteDeckCategory> categoriesByName)
    {
        List<string> names = [];
        if (!relation.TryGetProperty("categories", out JsonElement collection) ||
            collection.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (JsonElement value in collection.EnumerateArray())
        {
            string? candidate = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.Object => GetString(value, "name") ?? GetId(value, "id"),
                _ => null,
            };
            if (candidate is null)
            {
                continue;
            }

            string resolved = categoriesById.TryGetValue(candidate, out RemoteDeckCategory? byId)
                ? byId.Name
                : categoriesByName.TryGetValue(candidate, out RemoteDeckCategory? byName)
                    ? byName.Name
                    : candidate;
            if (!names.Contains(resolved, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(resolved);
            }
        }

        return names;
    }

    /// <summary>
    /// Maps provider category conventions into the format-neutral local zone vocabulary.
    /// </summary>
    private static string MapZone(
        IReadOnlyList<string> names,
        IReadOnlyDictionary<string, RemoteDeckCategory> categoriesByName)
    {
        foreach (string name in names)
        {
            if (name.Equals("commander", StringComparison.OrdinalIgnoreCase) ||
                (categoriesByName.TryGetValue(name, out RemoteDeckCategory? category) && category.IsPremier))
            {
                return "commander";
            }

            if (name.Equals("sideboard", StringComparison.OrdinalIgnoreCase))
            {
                return "sideboard";
            }

            if (name.Equals("maybeboard", StringComparison.OrdinalIgnoreCase))
            {
                return "maybeboard";
            }
        }

        return "main";
    }

    /// <summary>
    /// Recursively maps one folder node and its direct child relationships.
    /// </summary>
    private static void MapFolderNode(
        JsonElement item,
        string? inheritedParentId,
        string? inheritedPath,
        ICollection<RemoteFolderRecord> destination)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported("Archidekt folder payload is not an object.");
        }

        string folderId = RequireId(item, "id", "Archidekt folder identity is missing.");
        string name = RequireText(item, "name", "Archidekt folder name is missing.");
        string? parentId = GetId(item, "parent")
            ?? GetId(item, "parentFolder")
            ?? GetId(item, "parent_folder")
            ?? inheritedParentId;
        string path = GetString(item, "path")
            ?? (inheritedPath is null ? name : $"{inheritedPath}/{name}");
        List<string> childIds = [];
        List<JsonElement> childElements = [];
        if (item.TryGetProperty("children", out JsonElement children) &&
            children.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                childElements.Add(child);
                string? childId = GetId(child, "id");
                if (childId is not null)
                {
                    childIds.Add(childId);
                }
            }
        }

        List<RemoteDeckSummary> decks = [];
        if (item.TryGetProperty("decks", out JsonElement deckCollection) &&
            deckCollection.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement deck in deckCollection.EnumerateArray())
            {
                decks.Add(MapDeckSummary(deck));
            }
        }

        decks.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RemoteId, right.RemoteId));
        destination.Add(new RemoteFolderRecord(
            folderId,
            name,
            MapVisibility(item),
            parentId,
            path,
            childIds,
            decks,
            CopyUnknown(item, ["id", "name", "private", "unlisted", "parent", "parentFolder", "parent_folder", "path", "children", "decks"])));
        foreach (JsonElement child in childElements)
        {
            MapFolderNode(child, folderId, path, destination);
        }
    }

    /// <summary>
    /// Creates a fingerprint projection that excludes raw extension payloads.
    /// </summary>
    private static object FolderProjection(RemoteFolderRecord value)
    {
        return new
        {
            value.FolderId,
            value.Name,
            value.Visibility,
            value.ParentFolderId,
            value.Path,
            value.ChildFolderIds,
            decks = value.Decks.Select(deck => deck.RemoteId),
        };
    }

    /// <summary>
    /// Maps one named snapshot summary with exact source checksum and owning deck identity.
    /// </summary>
    private static RemoteNamedSnapshotSummary MapSnapshotSummary(JsonElement item, string expectedDeckId)
    {
        JsonElement snapshotMeta = item.TryGetProperty("snapshotMeta", out JsonElement meta) &&
            meta.ValueKind == JsonValueKind.Object
            ? meta
            : default;
        string snapshotId = RequireId(item, "id", "Archidekt snapshot identity is missing.");
        string deckId = GetId(snapshotMeta, "parentDeckId")
            ?? GetId(item, "deck")
            ?? GetId(item, "deckId")
            ?? GetNestedId(item, "deck", "id")
            ?? expectedDeckId;
        string name = RequireText(item, "name", "Archidekt snapshot name is missing.");
        Dictionary<string, JsonElement> extensions = CopyUnknown(
            item,
            ["id", "deck", "deckId", "name", "description", "createdAt", "updatedAt", "cards", "categories"]);
        string checksum = ArchidektContract.Hash(item.GetRawText());
        return new RemoteNamedSnapshotSummary(
            snapshotId,
            deckId,
            name,
            GetString(snapshotMeta, "description") ?? GetString(item, "description"),
            GetDateTime(item, "createdAt"),
            GetDateTime(item, "updatedAt"),
            checksum,
            extensions);
    }

    /// <summary>
    /// Corrects snapshot-only deck metadata and recomputes fingerprints without inventing unsaved visibility.
    /// </summary>
    private static RemoteDeckSnapshot RebuildSnapshotDeck(
        RemoteDeckSnapshot deck,
        string deckName)
    {
        string contentFingerprint = FingerprintContent(
            deckName,
            deck.Description,
            deck.Format,
            "not-recorded",
            parentFolderId: null,
            deck.Categories,
            deck.Entries);
        string remoteFingerprint = ArchidektContract.Fingerprint(new
        {
            deck.RemoteId,
            contentFingerprint,
            providerCategories = deck.Categories.Select(value => value.ProviderCategoryId),
            providerRelations = deck.Entries.Select(value => new
            {
                value.ProviderRelationId,
                value.ProviderCardId,
            }),
            deck.Extensions,
        });
        return deck with
        {
            Name = deckName,
            Visibility = "not-recorded",
            ParentFolderId = null,
            ContentFingerprint = contentFingerprint,
            RemoteFingerprint = remoteFingerprint,
        };
    }

    /// <summary>
    /// Computes content equality independently of provider-generated relation identifiers.
    /// </summary>
    private static string FingerprintContent(
        string name,
        string description,
        string format,
        string visibility,
        string? parentFolderId,
        IReadOnlyList<RemoteDeckCategory> categories,
        IReadOnlyList<RemoteDeckEntry> entries)
    {
        return ArchidektContract.Fingerprint(new
        {
            name,
            description,
            format,
            visibility,
            parentFolderId,
            categories = categories
                .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Name, StringComparer.Ordinal)
                .Select(value => new
                {
                    value.Name,
                    value.IncludedInDeck,
                    value.IncludedInPrice,
                    value.IsPremier,
                }),
            entries = entries.Select(value => new
            {
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.CategoryNames,
                value.PrimaryCategoryName,
                value.SortOrder,
            }),
        });
    }

    /// <summary>
    /// Copies provider fields that are not consumed by the normalized projection.
    /// </summary>
    private static Dictionary<string, JsonElement> CopyUnknown(
        JsonElement value,
        IReadOnlyCollection<string> knownFields)
    {
        Dictionary<string, JsonElement> extensions = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!knownFields.Contains(property.Name))
            {
                extensions[property.Name] = property.Value.Clone();
            }
        }

        return extensions;
    }

    /// <summary>
    /// Creates dated retrieval evidence with a UTC-normalized timestamp.
    /// </summary>
    private static ArchidektRetrievalEvidence Evidence(
        string method,
        DateTimeOffset retrievedAtUtc,
        string checksum)
    {
        return new ArchidektRetrievalEvidence(
            "archidekt",
            method,
            ArchidektContract.Version,
            retrievedAtUtc.ToUniversalTime(),
            checksum);
    }

    /// <summary>
    /// Enumerates a direct array or one of the observed collection properties.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateCollection(
        JsonElement root,
        params string[] propertyNames)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        foreach (string propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out JsonElement collection) &&
                collection.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in collection.EnumerateArray())
                {
                    yield return item;
                }

                yield break;
            }
        }
    }

    /// <summary>
    /// Selects an observed nested object when present, otherwise returns the supplied root.
    /// </summary>
    private static JsonElement UnwrapObject(JsonElement root, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(propertyName, out JsonElement nested) &&
                nested.ValueKind == JsonValueKind.Object)
            {
                return nested;
            }
        }

        return root;
    }

    /// <summary>
    /// Gets a required string-like provider identifier.
    /// </summary>
    private static string RequireId(JsonElement value, string propertyName, string message)
    {
        return GetId(value, propertyName) ?? throw Unsupported(message);
    }

    /// <summary>
    /// Gets a required nonblank provider text field.
    /// </summary>
    private static string RequireText(JsonElement value, string propertyName, string message)
    {
        return ArchidektContract.Optional(GetString(value, propertyName)) ?? throw Unsupported(message);
    }

    /// <summary>
    /// Reads a provider identifier represented as either string or number.
    /// </summary>
    private static string? GetId(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => ArchidektContract.Optional(property.GetString()),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.Object => GetId(property, "id"),
            _ => null,
        };
    }

    /// <summary>
    /// Reads a nested provider identifier.
    /// </summary>
    private static string? GetNestedId(JsonElement value, string objectName, string propertyName)
    {
        return value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(objectName, out JsonElement nested) &&
            nested.ValueKind == JsonValueKind.Object
            ? GetId(nested, propertyName)
            : null;
    }

    /// <summary>
    /// Reads a string or scalar provider value as text.
    /// </summary>
    private static string? GetString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => ArchidektContract.Optional(property.GetString()),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    /// <summary>
    /// Reads a nested string provider value.
    /// </summary>
    private static string? GetNestedString(JsonElement value, string objectName, string propertyName)
    {
        return value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(objectName, out JsonElement nested) &&
            nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, propertyName)
            : null;
    }

    /// <summary>
    /// Reads an integer provider value without accepting fractional values.
    /// </summary>
    private static int? GetInt32(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    /// <summary>
    /// Reads an optional provider boolean.
    /// </summary>
    private static bool? GetBoolean(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>
    /// Reads and UTC-normalizes an optional provider timestamp.
    /// </summary>
    private static DateTimeOffset? GetDateTime(JsonElement value, string propertyName)
    {
        string? text = GetString(value, propertyName);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset timestamp)
            ? timestamp
            : null;
    }

    /// <summary>
    /// Parses an optional official card identifier.
    /// </summary>
    private static Guid? ParseGuid(string? value)
    {
        return Guid.TryParse(value, out Guid result) ? result : null;
    }

    /// <summary>
    /// Maps numeric and named deck formats without claiming unknown values are Commander.
    /// </summary>
    private static string MapFormat(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "1" or "standard" => "standard",
            "2" or "modern" => "modern",
            "3" or "commander" or "edh" => "commander",
            "4" or "legacy" => "legacy",
            "5" or "vintage" => "vintage",
            "6" or "pauper" => "pauper",
            "7" or "pioneer" => "pioneer",
            "8" or "brawl" => "brawl",
            "9" or "historic" => "historic",
            "10" or "oathbreaker" => "oathbreaker",
            null or "" => "unknown",
            var other => other,
        };
    }

    /// <summary>
    /// Maps provider privacy flags into one explicit visibility value.
    /// </summary>
    private static string MapVisibility(JsonElement value)
    {
        if (GetBoolean(value, "private") == true)
        {
            return "private";
        }

        return GetBoolean(value, "unlisted") == true ? "unlisted" : "public";
    }

    /// <summary>
    /// Maps Archidekt modifier names into the local finish vocabulary.
    /// </summary>
    private static string MapFinish(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "foil" => "foil",
            "etched" or "etched foil" => "etched",
            _ => "nonfoil",
        };
    }

    /// <summary>
    /// Creates a fail-closed contract-drift exception with no provider payload details.
    /// </summary>
    private static ArchidektProviderException Unsupported(string message)
    {
        return new ArchidektProviderException(
            ArchidektFailureKind.Unsupported,
            "provider-contract-unsupported",
            message);
    }
}
