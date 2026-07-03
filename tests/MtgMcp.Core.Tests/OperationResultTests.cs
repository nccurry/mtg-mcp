using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies the closed operation-result contract and its wire representation.
/// </summary>
public sealed class OperationResultTests
{
    /// <summary>
    /// Provides the web serializer behavior used by the future MCP host.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies that every result case has a stable and distinct discriminator.
    /// </summary>
    [Fact]
    public void Cases_SerializeWithStableDiscriminators()
    {
        OperationResult<string>[] results =
        [
            new OperationSuccess<string>("value"),
            new OperationNotFound("missing", "The value was not found."),
            new OperationNotCached("cache-miss", "The value is not cached."),
            new OperationUnsupported("unsupported", "The operation is unsupported."),
            new OperationUnavailable("unavailable", "The source is unavailable."),
            new OperationConflict("conflict", "The state has changed."),
            new OperationInvalidInput("invalid", "The input is invalid."),
        ];
        string[] expectedKinds =
        [
            "success",
            "not-found",
            "not-cached",
            "unsupported",
            "unavailable",
            "conflict",
            "invalid-input",
        ];

        for (int index = 0; index < results.Length; index++)
        {
            string json = JsonSerializer.Serialize(results[index], SerializerOptions);
            using JsonDocument document = JsonDocument.Parse(json);
            OperationResult<string> roundTrip =
                JsonSerializer.Deserialize<OperationResult<string>>(json, SerializerOptions);

            Assert.Equal(expectedKinds[index], document.RootElement.GetProperty("kind").GetString());
            Assert.Equal(expectedKinds[index], Describe(results[index]));
            Assert.Equal(expectedKinds[index], Describe(roundTrip));
        }
    }

    /// <summary>
    /// Verifies missing, unknown, and empty union payloads fail instead of producing an invented state.
    /// </summary>
    [Fact]
    public void InvalidJsonOrEmptyUnion_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OperationResult<string>>("{}", SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OperationResult<string>>("null", SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OperationResult<string>>(
                "{\"kind\":42}",
                SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OperationResult<string>>(
                "{\"kind\":\"future-case\"}",
                SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Serialize(default(OperationResult<string>), SerializerOptions));

        OperationResultJsonConverterFactory factory = new();
        Assert.True(factory.CanConvert(typeof(OperationResult<string>)));
        Assert.False(factory.CanConvert(typeof(string)));
        Assert.False(factory.CanConvert(typeof(List<string>)));
    }

    /// <summary>
    /// Verifies that successful empty data is not represented as an unavailable state.
    /// </summary>
    [Fact]
    public void EmptyCollection_RemainsSuccessfulEmptyData()
    {
        OperationResult<IReadOnlyList<string>> result =
            new OperationSuccess<IReadOnlyList<string>>([]);

        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(result, SerializerOptions));

        Assert.Equal("success", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("data").GetArrayLength());
        Assert.False(document.RootElement.TryGetProperty("reasonCode", out _));
    }

    /// <summary>
    /// Verifies that failure cases expose only the stable public failure fields.
    /// </summary>
    [Fact]
    public void FailureCases_SerializeReasonCodeAndSanitizedMessageShape()
    {
        OperationResult<string>[] failures =
        [
            new OperationNotFound("missing", "Not found."),
            new OperationNotCached("cache-miss", "Not cached."),
            new OperationUnsupported("unsupported", "Unsupported."),
            new OperationUnavailable("unavailable", "Unavailable."),
            new OperationConflict("conflict", "Conflict."),
            new OperationInvalidInput("invalid", "Invalid."),
        ];

        foreach (OperationResult<string> failure in failures)
        {
            using JsonDocument document = JsonDocument.Parse(
                JsonSerializer.Serialize(failure, SerializerOptions));
            string[] propertyNames = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(["kind", "message", "reasonCode"], propertyNames);
        }
    }

    /// <summary>
    /// Exhaustively maps every closed result case for compile-time change detection.
    /// </summary>
    private static string Describe(OperationResult<string> result)
    {
        return result switch
        {
            OperationSuccess<string> value => value.Kind,
            OperationNotFound value => value.Kind,
            OperationNotCached value => value.Kind,
            OperationUnsupported value => value.Kind,
            OperationUnavailable value => value.Kind,
            OperationConflict value => value.Kind,
            OperationInvalidInput value => value.Kind,
        };
    }
}
