using System.Text.Json;
using MtgMcp.Core.Evidence;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies lossless mapping and opaque cursor contracts independently of persistence.
/// </summary>
public sealed class ScryfallContractTests
{
    /// <summary>
    /// Reuses the fixture color identity array.
    /// </summary>
    private static readonly string[] WhiteRed = ["W", "R"];

    /// <summary>
    /// Reuses the fixture keyword array.
    /// </summary>
    private static readonly string[] TransformKeyword = ["Transform"];

    /// <summary>
    /// Reuses the white face color array.
    /// </summary>
    private static readonly string[] White = ["W"];

    /// <summary>
    /// Reuses the red face color array.
    /// </summary>
    private static readonly string[] Red = ["R"];

    /// <summary>
    /// Verifies multi-face cards retain absent root groups, face projections, and unknown source fields.
    /// </summary>
    [Fact]
    public void CardMapper_PreservesFacesAbsentGroupsAndExtensions()
    {
        Guid cardId = Guid.Parse("12121212-1212-4212-8212-121212121212");
        Guid oracleId = Guid.Parse("13131313-1313-4313-8313-131313131313");
        Guid faceIllustration = Guid.Parse("14141414-1414-4414-8414-141414141414");
        object[] faces =
        [
            new
            {
                name = "Fixture",
                mana_cost = "{W}",
                type_line = "Creature — Human",
                oracle_text = "Front text.",
                colors = White,
                image_uris = new Dictionary<string, string> { ["normal"] = "https://img.test/front.jpg" },
                illustration_id = (Guid?)faceIllustration,
                extension = true,
            },
            new
            {
                name = "Back",
                mana_cost = (string?)null,
                type_line = "Creature — Warrior",
                oracle_text = "Back text.",
                colors = Red,
                image_uris = new Dictionary<string, string>(),
                illustration_id = (Guid?)null,
                extension = false,
            },
        ];
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            id = cardId,
            oracle_id = oracleId,
            name = "Fixture // Back",
            set = "tst",
            collector_number = "1",
            lang = "en",
            colors = Array.Empty<string>(),
            color_identity = WhiteRed,
            keywords = TransformKeyword,
            legalities = new Dictionary<string, string>(),
            prices = new Dictionary<string, string?>(),
            card_faces = faces,
            unknown_extension = "retained",
        }));
        DateTimeOffset retrieved = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

        ScryfallCard card = ScryfallMapper.Card(document.RootElement, retrieved, "snapshot-1");

        Assert.Equal(cardId, card.Id);
        Assert.Null(card.ManaCost);
        Assert.Null(card.OracleText);
        Assert.Empty(card.ImageUris);
        Assert.Equal(2, card.Faces.Count);
        Assert.Equal(faceIllustration, card.Faces[0].IllustrationId);
        Assert.True(card.Faces[0].Raw!.Value.GetProperty("extension").GetBoolean());
        Assert.Equal("retained", card.Raw!.Value.GetProperty("unknown_extension").GetString());
        SourceFactDescriptor evidence = Assert.IsType<SourceFactDescriptor>(card.Evidence.Value);
        Assert.Equal(retrieved, evidence.RetrievedAtUtc);
        Assert.Equal("snapshot-1", evidence.SnapshotId);
    }

    /// <summary>
    /// Verifies cursor payloads are scope/checksum bound and malformed text fails without throwing.
    /// </summary>
    [Fact]
    public void Cursor_BindsScopeChecksumAndOrdinal()
    {
        string cursor = ScryfallCursor.Encode("scope", "checksum", 25);

        Assert.True(ScryfallCursor.TryDecode(cursor, "scope", "checksum", out int offset));
        Assert.Equal(25, offset);
        Assert.False(ScryfallCursor.TryDecode(cursor, "other", "checksum", out _));
        Assert.False(ScryfallCursor.TryDecode(cursor, "scope", "other", out _));
        Assert.False(ScryfallCursor.TryDecode("%%%", "scope", "checksum", out _));
        Assert.True(ScryfallCursor.TryDecode(null, "scope", "checksum", out int initial));
        Assert.Equal(0, initial);
        Assert.Throws<ArgumentOutOfRangeException>(() => ScryfallCursor.Encode("scope", "checksum", -1));

        ScryfallCollectionCursorState collectionState = new(
            0,
            "request-hash",
            Guid.Parse("15151515-1515-4515-8515-151515151515"),
            Guid.Parse("16161616-1616-4616-8616-161616161616"),
            "snapshot-checksum",
            "result-checksum",
            "not-found",
            25);
        string collectionCursor = ScryfallCursor.EncodeCollection(collectionState);
        Assert.True(ScryfallCursor.TryDecodeCollection(
            collectionCursor,
            "request-hash",
            out ScryfallCollectionCursorState? decoded));
        Assert.Equal(1, decoded!.SchemaVersion);
        Assert.Equal(25, decoded.Offset);
        Assert.False(ScryfallCursor.TryDecodeCollection(collectionCursor, "other-request", out _));
        Assert.False(ScryfallCursor.TryDecodeCollection("%%%", "request-hash", out _));
        Assert.Throws<ArgumentException>(() => ScryfallCursor.EncodeCollection(
            collectionState with { SnapshotChecksum = null }));
    }

    /// <summary>
    /// Verifies malformed required provider fields fail instead of becoming invented defaults.
    /// </summary>
    [Fact]
    public void Mapper_RejectsMissingAndMalformedRequiredFields()
    {
        using JsonDocument missing = JsonDocument.Parse("{}");
        using JsonDocument malformed = JsonDocument.Parse("{\"id\":\"not-a-guid\"}");

        Assert.Throws<InvalidDataException>(() => ScryfallMapper.RequiredString(missing.RootElement, "name"));
        Assert.Throws<InvalidDataException>(() => ScryfallMapper.RequiredStringAllowEmpty(missing.RootElement, "comment"));
        Assert.Throws<InvalidDataException>(() => ScryfallMapper.RequiredGuid(malformed.RootElement, "id"));
        Assert.Null(ScryfallMapper.OptionalGuid(missing.RootElement, "id"));
        Assert.Empty(ScryfallMapper.Strings(missing.RootElement, "values"));
        Assert.Empty(ScryfallMapper.Guids(missing.RootElement, "values"));
    }

    /// <summary>
    /// Verifies a present empty ruling comment remains provider evidence rather than becoming a missing-field failure.
    /// </summary>
    [Fact]
    public void RulingMapper_PreservesPresentEmptyComment()
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            oracle_id = Guid.Parse("17171717-1717-4717-8717-171717171717"),
            source = "wotc",
            published_at = "2026-07-04",
            comment = string.Empty,
        }));

        ScryfallRuling ruling = ScryfallMapper.Ruling(
            document.RootElement,
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            "snapshot-1");

        Assert.Equal(string.Empty, ruling.Comment);
        Assert.Equal(string.Empty, ruling.Raw!.Value.GetProperty("comment").GetString());
    }
}
