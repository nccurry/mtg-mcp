using System.Text.Json;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Measures how many bytes a value takes when the server writes it as a tool result, so every tool cuts its results
/// against the size limit the same way.
/// </summary>
internal static class ResultSize
{
    /// <summary>
    /// Measures the serialized size of a value as the server writes it.
    /// </summary>
    internal static int Measure<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, value!.GetType(), ToolCallPolicy.SerializerOptions).Length;
    }
}
