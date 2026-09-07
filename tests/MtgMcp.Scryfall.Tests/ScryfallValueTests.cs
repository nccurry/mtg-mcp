namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies small fixed Scryfall values that are shared by multiple stores.
/// </summary>
public sealed class ScryfallValueTests
{
    /// <summary>
    /// Verifies request fingerprints use the established lowercase SHA-256 representation.
    /// </summary>
    [Fact]
    public void Hash_UsesLowercaseSha256()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ScryfallHash.Compute("abc"));
    }

    /// <summary>
    /// Verifies every accepted tag weight has its fixed ordering value.
    /// </summary>
    [Theory]
    [InlineData("weak", 0)]
    [InlineData("median", 1)]
    [InlineData("strong", 2)]
    [InlineData("very_strong", 3)]
    [InlineData("very-strong", 3)]
    [InlineData("unknown", -1)]
    public void TagWeight_RanksKnownValues(string weight, int expectedRank)
    {
        Assert.Equal(expectedRank, ScryfallTagWeight.Rank(weight));
    }
}
