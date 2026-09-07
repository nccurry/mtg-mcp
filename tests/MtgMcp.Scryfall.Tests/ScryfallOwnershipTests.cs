using System.Reflection;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Protects the boundary between shared SQLite support and Scryfall data domains.
/// </summary>
public sealed class ScryfallOwnershipTests
{
    /// <summary>
    /// Verifies the database keeps only connection, schema, and disposal work.
    /// </summary>
    [Fact]
    public void Database_DeclaresOnlyConnectionSchemaAndDisposalMethods()
    {
        string[] methodNames = typeof(ScryfallDatabase)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Dispose",
                "EnsureSchemaAsync",
                "OpenReadAsync",
                "OpenWriteAsync",
                "ValidateExistingSchemaAsync",
                "ValidateSchemaRowAsync",
            ],
            methodNames);
    }
}
