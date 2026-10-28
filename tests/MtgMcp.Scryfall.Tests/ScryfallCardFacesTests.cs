using System.Text.Json;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies the one place that reads the faces, types, costs, and symbols of a card.
/// </summary>
public sealed class ScryfallCardFacesTests
{
    /// <summary>
    /// Verifies an adventure card is cast for its front face cost, not the two costs Scryfall joins at the top
    /// (rule 715.4), while a split card keeps both costs.
    /// </summary>
    [Fact]
    public void FrontManaCost_AdventureUsesTheFrontFace_SplitKeepsBothCosts()
    {
        ScryfallCard adventure = Parse("""
            {"id":"aaaaaaaa-0000-4000-8000-000000000001","oracle_id":"bbbbbbbb-0000-4000-8000-000000000001",
             "name":"Giant // Stomp","set":"tst","collector_number":"1","lang":"en","layout":"adventure",
             "mana_cost":"{2}{R} // {1}{R}",
             "card_faces":[{"name":"Giant","mana_cost":"{2}{R}","type_line":"Creature — Giant"},
                           {"name":"Stomp","mana_cost":"{1}{R}","type_line":"Instant — Adventure"}]}
            """);
        ScryfallCard split = Parse("""
            {"id":"aaaaaaaa-0000-4000-8000-000000000002","oracle_id":"bbbbbbbb-0000-4000-8000-000000000002",
             "name":"Fire // Ice","set":"tst","collector_number":"2","lang":"en","layout":"split",
             "mana_cost":"{1}{R} // {1}{U}",
             "card_faces":[{"name":"Fire","mana_cost":"{1}{R}","type_line":"Instant"},
                           {"name":"Ice","mana_cost":"{1}{U}","type_line":"Instant"}]}
            """);

        Assert.Equal("{2}{R}", ScryfallCardFaces.FrontManaCost(adventure));
        Assert.Equal("{1}{R} // {1}{U}", ScryfallCardFaces.FrontManaCost(split));
    }

    /// <summary>
    /// Verifies a land is found on the front face only, or on any face, as the caller asks.
    /// </summary>
    [Fact]
    public void IsLand_FrontOnlyAndAnyFace_DiffersForADoubleFacedCard()
    {
        ScryfallCard spellFront = Parse("""
            {"id":"aaaaaaaa-0000-4000-8000-000000000003","oracle_id":"bbbbbbbb-0000-4000-8000-000000000003",
             "name":"Spell // Land","set":"tst","collector_number":"3","lang":"en","layout":"modal_dfc",
             "card_faces":[{"name":"Spell","type_line":"Sorcery"},{"name":"Land","type_line":"Land"}]}
            """);

        Assert.False(ScryfallCardFaces.IsLand(spellFront, frontOnly: true));
        Assert.True(ScryfallCardFaces.IsLand(spellFront, frontOnly: false));
        Assert.Equal(["2", "R", "W/P"], ScryfallCardFaces.ManaSymbols("{2}{R}{W/P}"));
        Assert.Empty(ScryfallCardFaces.ManaSymbols(null));
    }

    /// <summary>
    /// Reads one card from JSON.
    /// </summary>
    private static ScryfallCard Parse(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return ScryfallMapper.Card(document.RootElement, DateTimeOffset.UnixEpoch, "test", includeRaw: false);
    }
}
