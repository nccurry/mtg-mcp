using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.Core.Paging;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Hashes the request arguments that must stay the same from page to page, for
/// <see cref="PageCursorBinding.ForRequest"/>.
/// </summary>
internal static class RequestHash
{
    /// <summary>
    /// Hashes each part, serialized with the server's own options, in the order given.
    /// </summary>
    internal static string Of(params (object? Value, Type Type)[] parts)
    {
        StringBuilder text = new();
        foreach ((object? value, Type type) in parts)
        {
            text.Append(JsonSerializer.Serialize(value, type, ToolCallPolicy.SerializerOptions)).Append('\u001f');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..32];
    }
}
