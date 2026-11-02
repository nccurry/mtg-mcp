using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the shared page cursor codec: what it writes reads back, and every cursor that was edited, cut, issued for
/// another request, or issued for state that has since changed is refused with the right outcome.
/// </summary>
public sealed class PageCursorTests
{
    /// <summary>
    /// Holds a deck id used by the deck bindings.
    /// </summary>
    private static readonly Guid DeckId = Guid.Parse("0197a1b2-c3d4-7e5f-8091-a2b3c4d5e6f7");

    /// <summary>
    /// Gives each kind of binding, built twice with the same values, so a test can read a cursor against an equal
    /// binding.
    /// </summary>
    public static TheoryData<string> BindingKinds => ["deck", "preview", "remote", "artifact", "source", "request", "none"];

    /// <summary>
    /// Verifies a position written for each kind of binding reads back unchanged against an equal binding.
    /// </summary>
    [Theory]
    [MemberData(nameof(BindingKinds))]
    public void Decode_ACursorOfEachBindingKind_ReadsBackItsPosition(string kind)
    {
        string cursor = PageCursor.Encode(new PageCursorPosition(42), Binding(kind, "a"));

        OperationResult<PageCursorPosition> read = PageCursor.Decode(cursor, Binding(kind, "a"));

        Assert.Equal(new PageCursorPosition(42), Position(read));
    }

    /// <summary>
    /// Verifies two positions, the provider-sized maximum, and a combined binding survive a round trip.
    /// </summary>
    [Fact]
    public void Decode_TwoPositionsWithACombinedBinding_ReadsBackBoth()
    {
        PageCursorBinding binding = PageCursorBinding.ForDeck(DeckId, 7)
            .And(PageCursorBinding.ForRequest("hash"))
            .And(PageCursorBinding.ForPreview("fp"));
        string cursor = PageCursor.Encode(new PageCursorPosition(3, int.MaxValue), binding);

        OperationResult<PageCursorPosition> read = PageCursor.Decode(cursor, binding, twoLists: true);

        Assert.Equal(
            new PageCursorPosition(3, int.MaxValue),
            Position(read));
    }

    /// <summary>
    /// Verifies a missing cursor starts at the first row of one list or of both lists.
    /// </summary>
    [Fact]
    public void Decode_ANullCursor_StartsAtTheFirstRow()
    {
        PageCursorBinding binding = PageCursorBinding.None;

        Assert.Equal(
            new PageCursorPosition(0),
            Position(PageCursor.Decode(null, binding)));
        Assert.Equal(
            new PageCursorPosition(0, 0),
            Position(PageCursor.Decode(null, binding, twoLists: true)));
    }

    /// <summary>
    /// Verifies a cursor uses only URL-safe characters, so it can travel in a URL or a JSON string unescaped.
    /// </summary>
    [Fact]
    public void Encode_UsesOnlyUrlSafeCharacters()
    {
        string cursor = PageCursor.Encode(
            new PageCursorPosition(1_000_000, 2),
            PageCursorBinding.ForSource("???>>>~~~~"));

        Assert.All(cursor, character => Assert.True(
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.',
            $"'{character}' is not URL safe."));
    }

    /// <summary>
    /// Verifies text that is not a cursor is invalid input at the cursor argument.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a cursor")]
    [InlineData(".")]
    [InlineData("abc.")]
    [InlineData(".abc")]
    [InlineData("e30.e30")]
    [InlineData("!!!!.!!!!")]
    public void Decode_MalformedText_IsInvalidCursor(string cursor)
    {
        AssertInvalid(PageCursor.Decode(cursor, PageCursorBinding.None));
    }

    /// <summary>
    /// Verifies a cursor whose payload was edited, or whose checksum was changed, is refused.
    /// </summary>
    [Fact]
    public void Decode_AnEditedCursor_IsInvalidCursor()
    {
        PageCursorBinding binding = PageCursorBinding.ForSource("abc");
        string original = PageCursor.Encode(new PageCursorPosition(10), binding);
        string other = PageCursor.Encode(new PageCursorPosition(99), binding);
        string[] parts = original.Split('.');
        string otherPayload = other.Split('.')[0];

        AssertInvalid(PageCursor.Decode(otherPayload + "." + parts[1], binding));
        AssertInvalid(PageCursor.Decode(parts[0] + "." + other.Split('.')[1], binding));
        AssertInvalid(PageCursor.Decode(original[..^1], binding));
        AssertInvalid(PageCursor.Decode(original + "A", binding));
    }

    /// <summary>
    /// Verifies a cursor with a correct checksum but an unknown version, a negative position, or an odd shape is
    /// refused, so only what the codec itself writes is accepted.
    /// </summary>
    [Theory]
    [InlineData("""{"v":2,"p":[1],"b":{}}""")]
    [InlineData("""{"v":1,"p":[-1],"b":{}}""")]
    [InlineData("""{"v":1,"p":[],"b":{}}""")]
    [InlineData("""{"v":1,"p":[1,2,3],"b":{}}""")]
    [InlineData("""{"v":1,"p":["x"],"b":{}}""")]
    [InlineData("""{"v":1,"p":[1],"b":{"source":5}}""")]
    [InlineData("""{"v":1,"p":[1]}""")]
    [InlineData("[]")]
    public void Decode_AWellSignedPayloadOfTheWrongShape_IsInvalidCursor(string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        string cursor = Base64Url.EncodeToString(payload) + "." +
            Base64Url.EncodeToString(SHA256.HashData(payload).AsSpan(0, 8));

        AssertInvalid(PageCursor.Decode(cursor, PageCursorBinding.None));
    }

    /// <summary>
    /// Verifies a cursor of one list is refused by a result of two lists, and the other way round.
    /// </summary>
    [Fact]
    public void Decode_APositionCountThatDoesNotFitTheResult_IsInvalidCursor()
    {
        PageCursorBinding binding = PageCursorBinding.ForSource("abc");

        AssertInvalid(PageCursor.Decode(PageCursor.Encode(new PageCursorPosition(1), binding), binding, twoLists: true));
        AssertInvalid(PageCursor.Decode(PageCursor.Encode(new PageCursorPosition(1, 2), binding), binding));
    }

    /// <summary>
    /// Verifies a cursor issued for another request, deck, or artifact, or for other binding parts, is invalid input
    /// and not a stale conflict.
    /// </summary>
    [Theory]
    [InlineData("request")]
    [InlineData("deck")]
    [InlineData("artifact")]
    public void Decode_AnotherRequestOrSubject_IsInvalidCursor(string kind)
    {
        string cursor = PageCursor.Encode(new PageCursorPosition(5), Binding(kind, "a"));

        AssertInvalid(PageCursor.Decode(cursor, Binding(kind, "b")));
        AssertInvalid(PageCursor.Decode(cursor, PageCursorBinding.None));
        AssertInvalid(PageCursor.Decode(cursor, Binding(kind, "a").And(PageCursorBinding.ForSource("x"))));
    }

    /// <summary>
    /// Verifies a cursor whose state changed since it was issued is a stale conflict that carries the state the caller
    /// supplied, for every kind of state.
    /// </summary>
    [Theory]
    [InlineData("deck")]
    [InlineData("preview")]
    [InlineData("remote")]
    [InlineData("artifact")]
    [InlineData("source")]
    public void Decode_ChangedState_IsCursorStaleWithTheCurrentState(string kind)
    {
        string cursor = PageCursor.Encode(new PageCursorPosition(5), Binding(kind, "a", revision: 1));
        ConflictState current = new(Revision: 2, RemoteFingerprint: "now");

        OperationResult<PageCursorPosition> read = PageCursor.Decode(
            cursor,
            Binding(kind, "a", revision: 2, state: "b"),
            current: current);

        OperationConflict conflict = Assert.IsType<OperationConflict>(read.Value);
        Assert.Equal(SharedReasonCodes.CursorStale, conflict.ReasonCode);
        Assert.Equal(current, conflict.Current);
    }

    /// <summary>
    /// Verifies the size bound: a binding too large for a cursor cannot be written, a negative position cannot be
    /// written, and a read cursor longer than the bound is refused before it is decoded.
    /// </summary>
    [Fact]
    public void SizeBound_RefusesALargeBindingNegativePositionAndLongCursor()
    {
        PageCursorBinding large = PageCursorBinding.ForSource(new string('x', PageCursor.MaximumLength));

        Assert.Throws<ArgumentException>(() => PageCursor.Encode(new PageCursorPosition(1), large));
        Assert.Throws<ArgumentException>(() => PageCursor.Encode(new PageCursorPosition(-1), PageCursorBinding.None));
        Assert.Throws<ArgumentException>(() => PageCursor.Encode(new PageCursorPosition(1, -1), PageCursorBinding.None));
        AssertInvalid(PageCursor.Decode(new string('A', PageCursor.MaximumLength + 1), PageCursorBinding.None));
        Assert.True(
            PageCursor.Encode(
                new PageCursorPosition(1),
                PageCursorBinding.ForSource(new string('x', 512))).Length <= PageCursor.MaximumLength);
    }

    /// <summary>
    /// Verifies the request hash depends on the parts and their order, and is stable.
    /// </summary>
    [Fact]
    public void RequestHash_IsStable_AndDependsOnThePartsAndTheirOrder()
    {
        string first = PageCursor.RequestHash(("a", typeof(string)), (1, typeof(int)));

        Assert.Equal(first, PageCursor.RequestHash(("a", typeof(string)), (1, typeof(int))));
        Assert.NotEqual(first, PageCursor.RequestHash((1, typeof(int)), ("a", typeof(string))));
        Assert.NotEqual(first, PageCursor.RequestHash(("a", typeof(string)), (2, typeof(int))));
        Assert.Equal(32, first.Length);
    }

    /// <summary>
    /// Reads the position out of a successful decode.
    /// </summary>
    private static PageCursorPosition Position(OperationResult<PageCursorPosition> result)
    {
        return Assert.IsType<OperationSuccess<PageCursorPosition>>(result.Value).Data;
    }

    /// <summary>
    /// Asserts a result is the invalid-cursor refusal that names the cursor argument.
    /// </summary>
    private static void AssertInvalid(OperationResult<PageCursorPosition> result)
    {
        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal(SharedReasonCodes.InvalidCursor, invalid.ReasonCode);
        Assert.Equal("cursor", invalid.Path);
    }

    /// <summary>
    /// Builds the binding of one kind. The identity value is <paramref name="value"/>; the state values change with
    /// <paramref name="state"/> and <paramref name="revision"/>, so a test can build a binding whose state moved.
    /// </summary>
    private static PageCursorBinding Binding(string kind, string value, long revision = 1, string state = "s")
    {
        return kind switch
        {
            "deck" => PageCursorBinding.ForDeck(value == "a" ? DeckId : Guid.Empty, revision),
            "preview" => PageCursorBinding.ForPreview(state),
            "remote" => PageCursorBinding.ForRemote(state),
            "artifact" => PageCursorBinding.ForArtifact(value, state),
            "source" => PageCursorBinding.ForSource(state),
            "request" => PageCursorBinding.ForRequest(value),
            _ => PageCursorBinding.None,
        };
    }
}
