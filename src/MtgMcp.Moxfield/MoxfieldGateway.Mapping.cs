using System.Text.Json;
using System.Text.RegularExpressions;
using MtgMcp.Core;
using static MtgMcp.Core.MtgMcpJson;

namespace MtgMcp.Moxfield;

/// <summary>
/// Maps Moxfield deck JSON into provider-neutral workspaces.
/// </summary>
public sealed partial class MoxfieldGateway
{
    /// <summary>
    /// Lists Moxfield board names known to appear on deck responses.
    /// </summary>
    private static readonly string[] KnownBoardTypes =
    [
        "commanders",
        "partners",
        "mainboard",
        "maybeboard",
        "sideboard",
        "companions",
        "signatureSpells",
        "attractions",
        "contraptions",
        "planes",
        "schemes",
        "stickers",
        "tokens",
    ];

    /// <summary>
    /// Parses a Moxfield deck response.
    /// </summary>
    private static DeckWorkspace ParseDeck(JsonElement root, string deckId, string deckUrl)
    {
        Dictionary<string, string> deckTags = ParseDeckTags(root);
        Dictionary<string, List<string>> authorTags = ParseAuthorTags(root);
        List<string> warnings = [];
        DeckWorkspace workspace = new()
        {
            Name = GetString(root, "name") ?? $"Moxfield Deck {deckId}",
            Format = NormalizeFormat(GetString(root, "format")),
            Description = GetString(root, "description"),
            Mode = WorkspaceMode.Local,
            WriteBack = false,
            Warnings = warnings,
            SourceReferences =
            [
                new DeckSourceReference
                {
                    Provider = DeckImportProviders.Moxfield,
                    ExternalId = deckId,
                    Url = deckUrl,
                },
            ],
        };

        foreach ((string boardType, JsonElement board) in EnumerateBoards(root))
        {
            string primaryCategory = MapBoardCategory(boardType);
            bool includedInDeck = IsIncludedBoard(boardType);
            EnsureCategory(workspace, primaryCategory, includedInDeck);

            foreach (JsonElement relation in EnumerateBoardCards(board))
            {
                DeckCard? card = ParseCard(
                    relation,
                    boardType,
                    primaryCategory,
                    deckId,
                    deckTags,
                    authorTags,
                    warnings);
                if (card is null)
                {
                    continue;
                }

                workspace.Cards.Add(card);
                foreach (string category in card.Categories)
                {
                    EnsureCategory(
                        workspace,
                        category,
                        category.Equals(primaryCategory, StringComparison.OrdinalIgnoreCase)
                                ? includedInDeck
                                : false
                        );
                }
            }
        }

        workspace.Categories = workspace.Categories
            .GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return workspace;
    }

    /// <summary>
    /// Parses one board relation into a workspace card.
    /// </summary>
    private static DeckCard? ParseCard(
        JsonElement relation,
        string boardType,
        string primaryCategory,
        string deckId,
        IReadOnlyDictionary<string, string> deckTags,
        IReadOnlyDictionary<string, List<string>> authorTags,
        List<string> warnings
    )
    {
        JsonElement cardElement = relation.TryGetProperty("card", out JsonElement nestedCard)
            ? nestedCard
            : relation;
        string? name =
            GetString(cardElement, "name")
            ?? GetString(cardElement, "cardName")
            ?? GetString(relation, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            warnings.Add($"Skipped a {boardType} card without a name.");
            return null;
        }

        List<string> tags = ParseCardTags(relation, deckTags);
        if (authorTags.TryGetValue(name, out List<string>? cardAuthorTags))
        {
            AddTags(tags, cardAuthorTags);
        }

        List<string> categories = DeckCategoryOrdering.OrderedDistinct(primaryCategory, tags);
        DeckCard card = new()
        {
            Name = name,
            Quantity = Math.Max(1, GetInt(relation, "quantity") ?? GetInt(cardElement, "quantity") ?? 1),
            PrimaryCategory = categories[0],
            Categories = categories,
            ScryfallId = MtgMcpText.FirstNonEmpty(
                GetString(cardElement, "scryfall_id"),
                GetString(cardElement, "scryfallId")),
            ScryfallOracleId = MtgMcpText.FirstNonEmpty(
                GetString(cardElement, "oracle_id"),
                GetString(cardElement, "oracleId")),
            Companion = boardType.Equals("companions", StringComparison.OrdinalIgnoreCase),
            Modifier = NormalizeFinish(GetString(relation, "finish") ?? GetString(relation, "printing")),
            Snapshot = CreateSnapshot(cardElement),
            Metadata =
            {
                ["sourceProvider"] = DeckImportProviders.Moxfield,
                ["moxfieldDeckId"] = deckId,
                ["moxfieldBoardType"] = boardType,
            },
        };

        AddMetadata(card, "moxfieldCardId", GetString(cardElement, "id"));
        AddMetadata(card, "moxfieldFinish", GetString(relation, "finish"));
        AddMetadata(card, "moxfieldTags", tags.Count == 0 ? null : string.Join(", ", tags));
        return card;
    }

    /// <summary>
    /// Creates a card snapshot from Moxfield's Scryfall-like card fields.
    /// </summary>
    private static CardSnapshot CreateSnapshot(JsonElement cardElement)
    {
        return new CardSnapshot
        {
            ManaCost = GetString(cardElement, "mana_cost") ?? GetString(cardElement, "manaCost"),
            Layout = GetString(cardElement, "layout"),
            TypeLine = GetString(cardElement, "type_line") ?? GetString(cardElement, "typeLine"),
            ManaValue = GetDouble(cardElement, "cmc") ?? GetDouble(cardElement, "manaValue"),
            OracleText = GetString(cardElement, "oracle_text") ?? GetString(cardElement, "oracleText"),
            Power = GetString(cardElement, "power"),
            Toughness = GetString(cardElement, "toughness"),
            Loyalty = GetString(cardElement, "loyalty"),
            Defense = GetString(cardElement, "defense"),
            ColorIdentity = ReadStringArray(cardElement, "color_identity")
                .Concat(ReadStringArray(cardElement, "colorIdentity"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Set = GetString(cardElement, "set") ?? GetString(cardElement, "set_code"),
            CollectorNumber = GetString(cardElement, "cn") ?? GetString(cardElement, "collector_number"),
            Rarity = GetString(cardElement, "rarity"),
            EdhrecRank = GetInt(cardElement, "edhrec_rank") ?? GetInt(cardElement, "edhrecRank"),
            ScryfallUri = GetString(cardElement, "scryfall_uri") ?? GetString(cardElement, "scryfallUri"),
            Provenance = new CardSnapshotProvenance
            {
                Provider = DeckImportProviders.Moxfield,
                ProviderCardId = GetString(cardElement, "id"),
                SchemaVersion = 1,
                RefreshedAtUtc = DateTimeOffset.UtcNow,
            },
            Faces = ReadFaces(cardElement),
            Prices = ParsePrices(cardElement),
        };
    }

    /// <summary>
    /// Reads Scryfall-style card faces from Moxfield card payloads.
    /// </summary>
    private static List<CardFaceSnapshot> ReadFaces(JsonElement cardElement)
    {
        if (
            !cardElement.TryGetProperty("card_faces", out JsonElement faces)
            || faces.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<CardFaceSnapshot> result = [];
        foreach (JsonElement face in faces.EnumerateArray())
        {
            CardFaceSnapshot snapshot = new()
            {
                Name = GetString(face, "name"),
                ManaCost = GetString(face, "mana_cost") ?? GetString(face, "manaCost"),
                TypeLine = GetString(face, "type_line") ?? GetString(face, "typeLine"),
                OracleText = GetString(face, "oracle_text") ?? GetString(face, "oracleText"),
                Power = GetString(face, "power"),
                Toughness = GetString(face, "toughness"),
                Loyalty = GetString(face, "loyalty"),
                Defense = GetString(face, "defense"),
                Colors = ReadStringArray(face, "colors"),
            };
            result.Add(snapshot);
        }

        return result;
    }

    /// <summary>
    /// Enumerates Moxfield board objects from current and older response shapes.
    /// </summary>
    private static IEnumerable<(string BoardType, JsonElement Board)> EnumerateBoards(JsonElement root)
    {
        if (
            root.TryGetProperty("boards", out JsonElement boards)
            && boards.ValueKind == JsonValueKind.Object
        )
        {
            foreach (JsonProperty board in boards.EnumerateObject())
            {
                yield return (board.Name, board.Value);
            }

            yield break;
        }

        foreach (string boardType in KnownBoardTypes)
        {
            if (root.TryGetProperty(boardType, out JsonElement board))
            {
                yield return (boardType, board);
            }
        }
    }

    /// <summary>
    /// Enumerates cards from a Moxfield board object or array.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateBoardCards(JsonElement board)
    {
        if (board.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in board.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (board.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (board.TryGetProperty("cards", out JsonElement cards))
        {
            foreach (JsonElement card in EnumerateBoardCards(cards))
            {
                yield return card;
            }

            yield break;
        }

        foreach (JsonProperty property in board.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            yield return property.Value;
        }
    }

    /// <summary>
    /// Maps Moxfield board keys to provider-neutral category names.
    /// </summary>
    private static string MapBoardCategory(string boardType)
    {
        return boardType.Trim() switch
        {
            "commanders" or "partners" => DeckRoles.Commander,
            "mainboard" => DeckDefaults.Mainboard,
            "maybeboard" => DeckDefaults.Maybeboard,
            "sideboard" => DeckDefaults.Sideboard,
            "companions" => "Companion",
            "signatureSpells" => "Signature Spells",
            _ => ToTitleWords(boardType),
        };
    }

    /// <summary>
    /// Checks whether cards from a Moxfield board count toward the active deck.
    /// </summary>
    private static bool IsIncludedBoard(string boardType)
    {
        return boardType.Equals("mainboard", StringComparison.OrdinalIgnoreCase)
            || boardType.Equals("commanders", StringComparison.OrdinalIgnoreCase)
            || boardType.Equals("partners", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses deck-level tag id/name maps.
    /// </summary>
    private static Dictionary<string, string> ParseDeckTags(JsonElement root)
    {
        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("tags", out JsonElement tagElement))
        {
            return tags;
        }

        if (tagElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in tagElement.EnumerateObject())
            {
                string? name = property.Value.ValueKind == JsonValueKind.Object
                    ? GetString(property.Value, "name")
                    : property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    tags[property.Name] = name.Trim();
                }
            }
        }

        if (tagElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in tagElement.EnumerateArray())
            {
                string? id = GetString(item, "id");
                string? name = GetString(item, "name");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                {
                    tags[id] = name.Trim();
                }
            }
        }

        return tags;
    }

    /// <summary>
    /// Parses relation-level tag names.
    /// </summary>
    private static List<string> ParseCardTags(
        JsonElement relation,
        IReadOnlyDictionary<string, string> deckTags
    )
    {
        List<string> tags = [];
        foreach (string propertyName in new[] { "tags", "cardTags", "customTags", "tagNames" })
        {
            if (!relation.TryGetProperty(propertyName, out JsonElement tagElement))
            {
                continue;
            }

            AddTags(tags, tagElement, deckTags);
        }

        return tags;
    }

    /// <summary>
    /// Parses Moxfield's card-name to tag-name assignments.
    /// </summary>
    private static Dictionary<string, List<string>> ParseAuthorTags(JsonElement root)
    {
        Dictionary<string, List<string>> tags = new(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("authorTags", out JsonElement tagElement)
            || tagElement.ValueKind != JsonValueKind.Object)
        {
            return tags;
        }

        foreach (JsonProperty property in tagElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            List<string> cardTags = [];
            foreach (JsonElement item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    AddTag(cardTags, item.GetString());
                }
            }

            if (cardTags.Count > 0)
            {
                tags[property.Name] = cardTags;
            }
        }

        return tags;
    }

    /// <summary>
    /// Adds direct tag names with the same trimming and de-duplication as parsed JSON tags.
    /// </summary>
    private static void AddTags(List<string> tags, IEnumerable<string> tagNames)
    {
        foreach (string tagName in tagNames)
        {
            AddTag(tags, tagName);
        }
    }

    /// <summary>
    /// Adds tags from observed Moxfield array and map shapes.
    /// </summary>
    private static void AddTags(
        List<string> tags,
        JsonElement tagElement,
        IReadOnlyDictionary<string, string> deckTags
    )
    {
        if (tagElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in tagElement.EnumerateArray())
            {
                AddTag(tags, ReadTagName(item, deckTags));
            }
        }

        if (tagElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in tagElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.False)
                {
                    continue;
                }

                AddTag(tags, ReadTagName(property.Value, deckTags) ?? ResolveTagName(property.Name, deckTags));
            }
        }
    }

    /// <summary>
    /// Reads one tag name from a string, id, or object value.
    /// </summary>
    private static string? ReadTagName(
        JsonElement item,
        IReadOnlyDictionary<string, string> deckTags
    )
    {
        return item.ValueKind switch
        {
            JsonValueKind.String => ResolveTagName(item.GetString(), deckTags),
            JsonValueKind.Object => GetString(item, "name")
                ?? ResolveTagName(GetString(item, "id"), deckTags),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves a tag id to its display name while preserving direct names.
    /// </summary>
    private static string? ResolveTagName(
        string? value,
        IReadOnlyDictionary<string, string> deckTags
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return deckTags.TryGetValue(value, out string? name) ? name : value.Trim();
    }

    /// <summary>
    /// Adds a tag after trimming and case-insensitive de-duplication.
    /// </summary>
    private static void AddTag(List<string> tags, string? tag)
    {
        if (
            !string.IsNullOrWhiteSpace(tag)
            && !tags.Any(value => value.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase))
        )
        {
            tags.Add(tag.Trim());
        }
    }

    /// <summary>
    /// Ensures the workspace has a category with the requested inclusion policy.
    /// </summary>
    private static void EnsureCategory(
        DeckWorkspace workspace,
        string name,
        bool includedInDeck
    )
    {
        DeckCategory? existing = workspace.Categories.FirstOrDefault(category =>
            category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        );
        if (existing is not null)
        {
            return;
        }

        workspace.Categories.Add(new DeckCategory
        {
            Name = name,
            IncludedInDeck = includedInDeck,
            IncludedInPrice = true,
        });
    }

    /// <summary>
    /// Parses a price map and keeps USD-like values.
    /// </summary>
    private static Dictionary<string, string> ParsePrices(JsonElement cardElement)
    {
        Dictionary<string, string> prices = new(StringComparer.OrdinalIgnoreCase);
        if (
            !cardElement.TryGetProperty("prices", out JsonElement priceObject)
            || priceObject.ValueKind != JsonValueKind.Object
        )
        {
            return prices;
        }

        foreach (string key in new[] { "usd", "usd_foil", "usd_etched" })
        {
            string? value = GetString(priceObject, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                prices[key] = value;
            }
        }

        return prices;
    }

    /// <summary>
    /// Extracts the public Moxfield deck id from a raw id or deck URL.
    /// </summary>
    private static string ExtractDeckId(string deckIdOrUrl)
    {
        Match urlMatch = DeckUrlIdRegex().Match(deckIdOrUrl);
        if (urlMatch.Success)
        {
            return urlMatch.Groups["id"].Value;
        }

        Match idMatch = DeckIdRegex().Match(deckIdOrUrl);
        if (!idMatch.Success)
        {
            throw new ArgumentException(
                "Moxfield deck id or URL did not contain a public deck id.",
                nameof(deckIdOrUrl)
            );
        }

        return idMatch.Groups["id"].Value;
    }

    /// <summary>
    /// Builds the canonical browser URL for a Moxfield deck id.
    /// </summary>
    private static string ToDeckUrl(string deckId)
    {
        return $"https://www.moxfield.com/decks/{deckId}";
    }

    /// <summary>
    /// Normalizes Moxfield format text to mtg-mcp's workspace format values.
    /// </summary>
    private static string NormalizeFormat(string? format)
    {
        string normalized = format?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "" => "commander",
            "edh" => "commander",
            _ => normalized,
        };
    }

    /// <summary>
    /// Normalizes Moxfield finish values to Archidekt-style card modifiers where possible.
    /// </summary>
    private static string? NormalizeFinish(string? finish)
    {
        return finish?.Trim().ToLowerInvariant() switch
        {
            "foil" => "Foil",
            "etched" => "Etched",
            "normal" => "Normal",
            "" or null => null,
            _ => finish.Trim(),
        };
    }

    /// <summary>
    /// Converts camelCase board names into title-cased words.
    /// </summary>
    private static string ToTitleWords(string value)
    {
        string spaced = CamelBoundaryRegex().Replace(value.Trim(), " ");
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            spaced.ToLowerInvariant()
        );
    }

    /// <summary>
    /// Reads string arrays from Moxfield card fields.
    /// </summary>
    private static List<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property
                .GetString()
                ?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList() ?? [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    /// <summary>
    /// Adds metadata when the source value is present.
    /// </summary>
    private static void AddMetadata(DeckCard card, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            card.Metadata[key] = value.Trim();
        }
    }

    /// <summary>
    /// Matches raw Moxfield public ids.
    /// </summary>
    [GeneratedRegex(@"^(?<id>[A-Za-z0-9_-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DeckIdRegex();

    /// <summary>
    /// Matches Moxfield deck URLs.
    /// </summary>
    [GeneratedRegex(
        @"(?:^|/)decks/(?<id>[A-Za-z0-9_-]+)(?:[/?#]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DeckUrlIdRegex();

    /// <summary>
    /// Matches lower-to-upper boundaries in camelCase board names.
    /// </summary>
    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelBoundaryRegex();
}
