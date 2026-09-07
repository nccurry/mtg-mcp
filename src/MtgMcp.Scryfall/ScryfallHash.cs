using System.Security.Cryptography;
using System.Text;

namespace MtgMcp.Scryfall;

/// <summary>
/// Creates stable hashes for Scryfall cache and evidence identities.
/// </summary>
internal static class ScryfallHash
{
    /// <summary>
    /// Computes a lowercase UTF-8 SHA-256 digest.
    /// </summary>
    internal static string Compute(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
