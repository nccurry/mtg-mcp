using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Core.Evidence;

namespace MtgMcp.Statistics;

/// <summary>
/// Carries one completed exact calculation.
/// </summary>
public sealed record StatisticsExact<T>(T Data)
{
    /// <summary>
    /// Gets the stable nested outcome discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "exact";
}

/// <summary>
/// Describes the exact request bound that prevented a complete calculation.
/// </summary>
public sealed record StatisticsLimitDetail(
    string LimitKind,
    long Limit,
    long? EstimatedWork,
    int Population,
    int GroupCount,
    int TurnCount,
    int AttemptCount,
    IReadOnlyList<string> ReductionOptions)
{
    /// <summary>
    /// Gets an immutable copy of mechanical request-reduction options.
    /// </summary>
    public IReadOnlyList<string> ReductionOptions { get; init; } =
        Array.AsReadOnly(ReductionOptions.ToArray());
}

/// <summary>
/// Reports that a supported calculation exceeded one deterministic exact-work bound.
/// </summary>
public sealed record StatisticsBoundedUnsupported(
    string ReasonCode,
    string Message,
    StatisticsLimitDetail Limit)
{
    /// <summary>
    /// Gets the stable nested outcome discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "bounded-unsupported";
}

/// <summary>
/// Represents either one complete exact result or one structured bounded-work outcome.
/// </summary>
[JsonConverter(typeof(StatisticsCalculationJsonConverterFactory))]
public readonly union StatisticsCalculation<T>(
    StatisticsExact<T>,
    StatisticsBoundedUnsupported);

/// <summary>
/// Creates converters that serialize a calculation as its active nested case.
/// </summary>
public sealed class StatisticsCalculationJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType &&
            typeToConvert.GetGenericTypeDefinition() == typeof(StatisticsCalculation<>);
    }

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type dataType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(StatisticsCalculationJsonConverter<>).MakeGenericType(dataType);
        return (JsonConverter)(Activator.CreateInstance(converterType) ??
            throw new InvalidOperationException("The statistics-calculation converter could not be created."));
    }

    /// <summary>
    /// Serializes and deserializes one closed statistics-calculation type.
    /// </summary>
    private sealed class StatisticsCalculationJsonConverter<T> : JsonConverter<StatisticsCalculation<T>>
    {
        /// <inheritdoc/>
        public override StatisticsCalculation<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A statistics calculation must be a JSON object.");
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (!document.RootElement.TryGetProperty("kind", out JsonElement kindElement) ||
                kindElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("The statistics calculation is missing a string kind discriminator.");
            }

            return kindElement.GetString() switch
            {
                "exact" => ReadCase<StatisticsExact<T>>(document.RootElement, options),
                "bounded-unsupported" =>
                    ReadCase<StatisticsBoundedUnsupported>(document.RootElement, options),
                _ => throw new JsonException("The statistics calculation kind is unknown."),
            };
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            StatisticsCalculation<T> value,
            JsonSerializerOptions options)
        {
            object activeCase = value.Value ??
                throw new JsonException("A statistics calculation must contain an active case.");
            JsonSerializer.Serialize(writer, activeCase, activeCase.GetType(), options);
        }

        /// <summary>
        /// Deserializes one nonnull nested calculation case.
        /// </summary>
        private static TCase ReadCase<TCase>(JsonElement element, JsonSerializerOptions options)
            where TCase : notnull
        {
            return element.Deserialize<TCase>(options) ??
                throw new JsonException("The statistics calculation case payload is null.");
        }
    }
}

/// <summary>
/// Identifies one replayable exact derivation and implementation revision.
/// </summary>
public sealed record StatisticsDerivation(
    string FormulaId,
    string CalculationVersion,
    string ImplementationVersion,
    ExactDerivationDescriptor Evidence);

/// <summary>
/// Accounts for exact work across every composed operation in one request.
/// </summary>
internal sealed class StatisticsWorkBudget
{
    /// <summary>
    /// Defines the stable production request-wide work limit.
    /// </summary>
    internal const long DefaultLimit = 1_000_000;

    /// <summary>
    /// Creates one request budget.
    /// </summary>
    internal StatisticsWorkBudget(long limit = DefaultLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Limit = limit;
    }

    /// <summary>
    /// Gets consumed work units.
    /// </summary>
    internal long Used { get; private set; }

    /// <summary>
    /// Gets the configured work limit.
    /// </summary>
    internal long Limit { get; }

    /// <summary>
    /// Consumes positive units when doing so does not exceed the limit.
    /// </summary>
    internal bool TryConsume(long units)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(units);
        if (units > Limit - Used)
        {
            return false;
        }

        Used += units;
        return true;
    }

    /// <summary>
    /// Multiplies nonnegative estimates while saturating at <see cref="long.MaxValue"/>.
    /// </summary>
    internal static long SaturatingMultiply(long left, long right)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        if (left == 0 || right == 0)
        {
            return 0;
        }

        return left > long.MaxValue / right ? long.MaxValue : left * right;
    }
}
