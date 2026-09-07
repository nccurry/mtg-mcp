using System.Security.Cryptography;
using System.Text;

namespace MtgMcp.Spellbook;

/// <summary>
/// Creates stable SHA-256 values for Commander Spellbook cache and source evidence.
/// </summary>
internal static class SpellbookHash
{
    /// <summary>
    /// Computes a lowercase SHA-256 value for UTF-8 text.
    /// </summary>
    internal static string Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
