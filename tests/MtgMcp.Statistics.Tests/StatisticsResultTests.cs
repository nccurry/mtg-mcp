using System.Text.Json;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies nested exact and bounded calculation union serialization.
/// </summary>
public sealed class StatisticsResultTests
{
    /// <summary>
    /// Verifies exact and bounded cases round-trip through their stable discriminators.
    /// </summary>
    [Fact]
    public void StatisticsCalculation_RoundTripsEveryCase()
    {
        StatisticsCalculation<string>[] values =
        [
            new StatisticsExact<string>("value"),
            new StatisticsBoundedUnsupported(
                "statistics-bound-exceeded",
                "Bounded.",
                new StatisticsLimitDetail(
                    "population",
                    1_000,
                    1_001,
                    1_001,
                    2,
                    0,
                    0,
                    ["Reduce the population."])),
        ];

        foreach (StatisticsCalculation<string> value in values)
        {
            string json = JsonSerializer.Serialize(value);
            StatisticsCalculation<string> roundTrip =
                JsonSerializer.Deserialize<StatisticsCalculation<string>>(json);
            Assert.Equal(json, JsonSerializer.Serialize(roundTrip));
        }
    }

    /// <summary>
    /// Verifies malformed calculation JSON and an empty union fail closed.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"kind\":1}")]
    [InlineData("{\"kind\":\"unknown\"}")]
    public void StatisticsCalculation_MalformedJsonThrows(string json)
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<StatisticsCalculation<string>>(json));
    }

    /// <summary>
    /// Verifies converter discovery and an inactive union cannot serialize.
    /// </summary>
    [Fact]
    public void StatisticsCalculation_ConverterRejectsUnrelatedAndInactiveTypes()
    {
        StatisticsCalculationJsonConverterFactory factory = new();

        Assert.True(factory.CanConvert(typeof(StatisticsCalculation<string>)));
        Assert.False(factory.CanConvert(typeof(string)));
        Assert.NotNull(factory.CreateConverter(
            typeof(StatisticsCalculation<string>),
            new JsonSerializerOptions()));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(default(StatisticsCalculation<string>)));
    }
}
