namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies the pure set function over card lists: every operation, every match mode, and a stable order (SET-001,
/// cases C01 and C02).
/// </summary>
public sealed class ScryfallCardSetTests
{
    /// <summary>
    /// Verifies each of the four operations over {A, B, C} and {B, C, D} matching by Oracle card (case C01).
    /// </summary>
    [Theory]
    [InlineData(ScryfallSetOperation.Union, "A,B,C,D")]
    [InlineData(ScryfallSetOperation.Intersection, "B,C")]
    [InlineData(ScryfallSetOperation.Difference, "A")]
    [InlineData(ScryfallSetOperation.SymmetricDifference, "A,D")]
    public void Combine_EachOperation_GivesTheExactSet(ScryfallSetOperation operation, string expected)
    {
        (IReadOnlyList<ScryfallCard> left, IReadOnlyList<ScryfallCard> right) = ABCAndBCD();

        IReadOnlyList<ScryfallCard> result = ScryfallCardSets.Combine(left, right, operation, ScryfallMatchBy.Oracle);

        Assert.Equal(expected.Split(','), result.Select(card => card.Name));
    }

    /// <summary>
    /// Verifies two printings of one card match by Oracle card and by name but not by printing (case C02).
    /// </summary>
    [Fact]
    public void Combine_TwoPrintingsOfOneCard_MatchOnlyWhenPrintingIsNotTheKey()
    {
        Guid oracle = Guid.Parse("22222222-0000-4000-8000-000000000001");
        ScryfallCard first = ListTestCards.Make("X", oracleId: oracle);
        ScryfallCard second = ListTestCards.Make("X", oracleId: oracle);

        Assert.Single(Intersect(first, second, ScryfallMatchBy.Oracle));
        Assert.Single(Intersect(first, second, ScryfallMatchBy.Name));
        Assert.Empty(Intersect(first, second, ScryfallMatchBy.Printing));
        Assert.Equal(
            2,
            ScryfallCardSets.Combine([first], [second], ScryfallSetOperation.Union, ScryfallMatchBy.Printing).Count);
    }

    /// <summary>
    /// Verifies the result card comes from the left list when the card is in both, and each card appears once even
    /// when a list holds it twice.
    /// </summary>
    [Fact]
    public void Combine_CardInBothLists_ComesFromTheLeftAndAppearsOnce()
    {
        Guid oracle = Guid.Parse("22222222-0000-4000-8000-000000000002");
        ScryfallCard left = ListTestCards.Make("Same", oracleId: oracle);
        ScryfallCard leftTwin = ListTestCards.Make("Same", oracleId: oracle);
        ScryfallCard right = ListTestCards.Make("Same", oracleId: oracle);

        IReadOnlyList<ScryfallCard> result = ScryfallCardSets.Combine(
            [leftTwin, left],
            [right],
            ScryfallSetOperation.Union,
            ScryfallMatchBy.Oracle);

        ScryfallCard only = Assert.Single(result);
        Assert.Contains(only.Id, new[] { left.Id, leftTwin.Id });
        Assert.NotEqual(right.Id, only.Id);
    }

    /// <summary>
    /// Verifies results are ordered by name, then Oracle ID, whatever order the lists arrive in.
    /// </summary>
    [Fact]
    public void Combine_ResultOrder_IsByNameThenOracleIdAndIgnoresInputOrder()
    {
        ScryfallCard bravo = ListTestCards.Make("bravo");
        ScryfallCard alpha = ListTestCards.Make("Alpha");
        ScryfallCard charlie = ListTestCards.Make("Charlie");

        IReadOnlyList<ScryfallCard> forward = ScryfallCardSets.Combine(
            [bravo, alpha],
            [charlie],
            ScryfallSetOperation.Union,
            ScryfallMatchBy.Oracle);
        IReadOnlyList<ScryfallCard> backward = ScryfallCardSets.Combine(
            [charlie],
            [alpha, bravo],
            ScryfallSetOperation.Union,
            ScryfallMatchBy.Oracle);

        Assert.Equal(["Alpha", "bravo", "Charlie"], forward.Select(card => card.Name));
        Assert.Equal(forward.Select(card => card.Id), backward.Select(card => card.Id));
    }

    /// <summary>
    /// Verifies a name key ignores letter case, extra spaces, and Unicode form.
    /// </summary>
    [Fact]
    public void KeyOf_NameMode_NormalizesCaseSpacesAndUnicodeForm()
    {
        ScryfallCard plain = ListTestCards.Make("Nazgûl");
        ScryfallCard decomposed = ListTestCards.Make("  naZgûl ");

        Assert.Equal(
            ScryfallCardSets.KeyOf(plain, ScryfallMatchBy.Name),
            ScryfallCardSets.KeyOf(decomposed, ScryfallMatchBy.Name));
        Assert.Equal("a b", ScryfallCardSets.NormalizeName("A   B"));
    }

    /// <summary>
    /// Verifies the operation and match mode names read back to their values, and unknown names are refused.
    /// </summary>
    [Fact]
    public void Names_ParseEveryPublishedValueAndRefuseOthers()
    {
        foreach (string name in ScryfallSetNames.Operations)
        {
            Assert.True(ScryfallSetNames.TryParseOperation(name, out _));
        }

        foreach (string name in ScryfallSetNames.MatchModes)
        {
            Assert.True(ScryfallSetNames.TryParseMatchBy(name, out _));
        }

        Assert.True(ScryfallSetNames.TryParseMatchBy(null, out ScryfallMatchBy defaulted));
        Assert.Equal(ScryfallMatchBy.Oracle, defaulted);
        Assert.False(ScryfallSetNames.TryParseOperation("merge", out _));
        Assert.False(ScryfallSetNames.TryParseMatchBy("fuzzy", out _));
    }

    /// <summary>
    /// Intersects two single-card lists with a match mode.
    /// </summary>
    private static IReadOnlyList<ScryfallCard> Intersect(ScryfallCard left, ScryfallCard right, ScryfallMatchBy matchBy)
    {
        return ScryfallCardSets.Combine([left], [right], ScryfallSetOperation.Intersection, matchBy);
    }

    /// <summary>
    /// Builds the left list {A, B, C} and the right list {B, C, D}, where shared cards have the same Oracle ID.
    /// </summary>
    private static (IReadOnlyList<ScryfallCard> Left, IReadOnlyList<ScryfallCard> Right) ABCAndBCD()
    {
        Guid[] ids =
        [
            Guid.Parse("33333333-0000-4000-8000-00000000000a"),
            Guid.Parse("33333333-0000-4000-8000-00000000000b"),
            Guid.Parse("33333333-0000-4000-8000-00000000000c"),
            Guid.Parse("33333333-0000-4000-8000-00000000000d"),
        ];
        ScryfallCard Card(string name, int index)
        {
            return ListTestCards.Make(name, oracleId: ids[index]);
        }

        return ([Card("A", 0), Card("B", 1), Card("C", 2)], [Card("B", 1), Card("C", 2), Card("D", 3)]);
    }
}
