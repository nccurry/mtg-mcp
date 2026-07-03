using System.Text.Json;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Covers shared JSON readers used by adapter mapping code.
/// </summary>
public sealed class JsonTests
{
    /// <summary>
    /// Verifies string-like fields preserve JSON scalars as text.
    /// </summary>
    [Fact]
    public void GetString_ReturnsStringsAndRawScalarText()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "name": "Sol Ring", "id": 7, "missing": null }""");

        MtgMcpJson.GetString(document.RootElement, "name").Should().Be("Sol Ring");
        MtgMcpJson.GetString(document.RootElement, "id").Should().Be("7");
        MtgMcpJson.GetString(document.RootElement, "missing").Should().BeNull();
        MtgMcpJson.GetString(document.RootElement, "absent").Should().BeNull();
    }

    /// <summary>
    /// Verifies numeric readers can preserve strict provider behavior when requested.
    /// </summary>
    [Fact]
    public void GetInt_RespectsStringParsingOption()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "numeric": 42, "text": "42" }""");

        MtgMcpJson.GetInt(document.RootElement, "numeric", allowString: false).Should().Be(42);
        MtgMcpJson.GetInt(document.RootElement, "text").Should().Be(42);
        MtgMcpJson.GetInt(document.RootElement, "text", allowString: false).Should().BeNull();
    }

    /// <summary>
    /// Verifies common and provider-specific collection envelopes are enumerated.
    /// </summary>
    [Fact]
    public void EnumerateCollection_ReadsRootArraysAndNamedEnvelopes()
    {
        using JsonDocument rootArray = JsonDocument.Parse("""[{ "id": 1 }, { "id": 2 }]""");
        using JsonDocument defaultEnvelope = JsonDocument.Parse("""{ "data": [{ "id": 3 }] }""");
        using JsonDocument namedEnvelope = JsonDocument.Parse("""{ "decks": [{ "id": 4 }] }""");

        MtgMcpJson.EnumerateCollection(rootArray.RootElement)
            .Select(item => MtgMcpJson.GetInt(item, "id"))
            .Should().Equal(1, 2);
        MtgMcpJson.EnumerateCollection(defaultEnvelope.RootElement)
            .Select(item => MtgMcpJson.GetInt(item, "id"))
            .Should().Equal(3);
        MtgMcpJson.EnumerateCollection(namedEnvelope.RootElement, "decks")
            .Select(item => MtgMcpJson.GetInt(item, "id"))
            .Should().Equal(4);
    }

    /// <summary>
    /// Verifies nested and array readers preserve adapter-tolerant scalar handling.
    /// </summary>
    [Fact]
    public void NestedAndArrayReaders_ReadTolerantValues()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{ "card": { "rank": "7", "legal": "true" }, "colors": ["W", 1, true] }""");

        MtgMcpJson.GetNestedInt(document.RootElement, "card", "rank").Should().Be(7);
        MtgMcpJson.GetBool(document.RootElement.GetProperty("card"), "legal").Should().BeTrue();
        MtgMcpJson.GetBool(document.RootElement.GetProperty("card"), "missing", defaultValue: true)
            .Should().BeTrue();
        MtgMcpJson.GetStringArray(document.RootElement, "colors").Should().Equal("W", "1", "true");
    }
}
