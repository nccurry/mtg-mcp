using System.Text.Json.Serialization;

namespace MtgMcp.Core.Results;

/// <summary>
/// Carries the data produced by a successful operation, including successful empty collections.
/// </summary>
public sealed record OperationSuccess<T>(
    [property: JsonPropertyName("data")] T Data)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "success";
}

/// <summary>
/// Reports that the requested entity does not exist in the consulted source.
/// </summary>
public sealed record OperationNotFound(
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "not-found";
}

/// <summary>
/// Reports that an operation requires cached data that is not locally available.
/// </summary>
public sealed record OperationNotCached(
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "not-cached";
}

/// <summary>
/// Reports that the requested behavior is outside the available contract.
/// </summary>
public sealed record OperationUnsupported(
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "unsupported";
}

/// <summary>
/// Reports that a supported dependency or source cannot currently answer the request.
/// </summary>
public sealed record OperationUnavailable(
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "unavailable";
}

/// <summary>
/// Reports that the requested operation conflicts with current state.
/// </summary>
public sealed record OperationConflict(
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "conflict";
}

/// <summary>
/// Reports that caller input cannot be accepted by the operation contract.
/// </summary>
public sealed record OperationInvalidInput(
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "invalid-input";
}

/// <summary>
/// Represents the complete set of common operation outcomes without collapsing unknown states into empty data.
/// </summary>
[JsonConverter(typeof(OperationResultJsonConverterFactory))]
public readonly union OperationResult<T>(
    OperationSuccess<T>,
    OperationNotFound,
    OperationNotCached,
    OperationUnsupported,
    OperationUnavailable,
    OperationConflict,
    OperationInvalidInput);
