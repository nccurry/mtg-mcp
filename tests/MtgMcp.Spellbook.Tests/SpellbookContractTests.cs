using System.Security.Cryptography;
using System.Text.Json;

namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Verifies the pinned Commander Spellbook contract, input boundaries, and evidence model.
/// </summary>
public sealed class SpellbookContractTests
{
    /// <summary>
    /// Verifies the narrow checked-in contract fixture matches its recorded version and checksum.
    /// </summary>
    [Fact]
    public void OpenApiFixture_MatchesReviewedRoutesAndChecksum()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MtgMcp.Spellbook",
            "Fixtures",
            "OpenApi",
            "api-6.3.3.json");
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);

        Assert.Equal(
            SpellbookContract.OpenApiChecksum,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(SpellbookContract.ApiVersion, document.RootElement.GetProperty("apiVersion").GetString());
        Assert.Equal("2026-09-07", document.RootElement.GetProperty("reviewedAtUtc").GetString());
        JsonElement[] routes = document.RootElement.GetProperty("routes").EnumerateArray().ToArray();
        Assert.Equal(3, routes.Length);
        Assert.Contains(routes, route => route.GetProperty("path").GetString() == "/variants/");
        Assert.Contains(routes, route => route.GetProperty("path").GetString() == "/variants/{id}/");
        JsonElement deckRoute = Assert.Single(routes, route => route.GetProperty("path").GetString() == "/find-my-combos");
        Assert.Equal(JsonValueKind.Object, deckRoute.GetProperty("response").GetProperty("results").ValueKind);
        Assert.True(deckRoute.GetProperty("response").GetProperty("results").TryGetProperty(
            "almostIncludedByAddingColorsAndChangingCommanders",
            out _));
    }

    /// <summary>
    /// Verifies source input is preserved, page defaults are explicit, and deck rows are grouped ordinally.
    /// </summary>
    [Fact]
    public void ContractHelpers_PreserveSourceValuesAndNormalizeDeckRows()
    {
        const string sourceQuery = " card:\"Thassa's Oracle\" (blue white) ";

        Assert.Equal(sourceQuery, SpellbookContract.RequiredSourceQuery(sourceQuery, "query"));
        Assert.Null(SpellbookContract.OptionalSourceQuery(null, "query"));
        Assert.Equal(
            new SpellbookPageRequest(20, 0, true),
            SpellbookContract.Page(null));
        Assert.Equal(
            new SpellbookPageRequest(7, 3, false),
            SpellbookContract.Page(new SpellbookPageOptions(7, 3, false)));

        SpellbookDeckRequest normalized = SpellbookContract.Deck(new SpellbookDeckRequest(
            [new SpellbookDeckEntry("Zeta", 1), new SpellbookDeckEntry("Alpha", 2), new SpellbookDeckEntry("Alpha", 3)],
            [new SpellbookDeckEntry("Sol Ring", 1)]));

        Assert.Collection(
            normalized.Commanders,
            entry => Assert.Equal(new SpellbookDeckEntry("Alpha", 5), entry),
            entry => Assert.Equal(new SpellbookDeckEntry("Zeta", 1), entry));
        Assert.Equal(new SpellbookDeckEntry("Sol Ring", 1), Assert.Single(normalized.Main));
        JsonElement data = SpellbookContract.ParseResponseObject("{\"unknown\":null}");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("unknown").ValueKind);
    }

    /// <summary>
    /// Verifies invalid source and deck boundaries become safe expected adapter failures.
    /// </summary>
    [Fact]
    public void ContractHelpers_RejectInvalidBoundaries()
    {
        Assert.Equal("invalid-source-query", Assert.Throws<SpellbookProviderException>(
            () => SpellbookContract.RequiredSourceQuery(" ", "query")).ReasonCode);
        Assert.Equal("invalid-page-options", Assert.Throws<SpellbookProviderException>(
            () => SpellbookContract.Page(new SpellbookPageOptions(26))).ReasonCode);
        Assert.Equal("invalid-variant-id", Assert.Throws<SpellbookProviderException>(
            () => SpellbookContract.RequiredVariantId(" ", "id")).ReasonCode);
        Assert.Equal("empty-deck-request", Assert.Throws<SpellbookProviderException>(
            () => SpellbookContract.Deck(new SpellbookDeckRequest([], []))).ReasonCode);
        Assert.Equal("provider-contract-unsupported", Assert.Throws<SpellbookProviderException>(
            () => SpellbookContract.ParseResponseObject("[]")).ReasonCode);
        Assert.Equal("provider-contract-unsupported", Assert.Throws<SpellbookProviderException>(
            () => SpellbookContract.ParseResponseObject("not-json")).ReasonCode);
    }

    /// <summary>
    /// Verifies public cache options reject invalid local freshness settings.
    /// </summary>
    [Fact]
    public void Options_EnforceWholeMinuteFreshnessBounds()
    {
        SpellbookOptions defaults = SpellbookOptions.CreateDefault(".");

        Assert.Equal(TimeSpan.FromMinutes(15), defaults.CacheTtl);
        defaults.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { CacheTtl = TimeSpan.Zero }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { CacheTtl = TimeSpan.FromSeconds(90) }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with { CacheTtl = TimeSpan.FromMinutes(1_441) }).Validate());
    }

    /// <summary>
    /// Verifies source JSON and limitations become independent evidence values.
    /// </summary>
    [Fact]
    public void Evidence_DefensivelyCopiesSourceDataAndLimitations()
    {
        string[] limitations = ["first"];
        using JsonDocument document = JsonDocument.Parse("{\"extension\":true}");
        SpellbookEvidence evidence = new(
            "Commander Spellbook",
            "variant-search",
            new SpellbookRequestDetails("q", new SpellbookPageRequest(20, 0, true), null),
            "GET /variants/",
            SpellbookContract.ApiVersion,
            SpellbookContract.OpenApiChecksum,
            DateTimeOffset.UtcNow,
            "network",
            SpellbookHash.Compute("{}"),
            SpellbookContract.SourceUrl,
            limitations,
            document.RootElement);
        limitations[0] = "changed";

        Assert.Equal("first", Assert.Single(evidence.Limitations));
        Assert.True(evidence.Data.GetProperty("extension").GetBoolean());
    }

    /// <summary>
    /// Finds the repository root from the compiled test output directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mtg-mcp repository root.");
    }
}
