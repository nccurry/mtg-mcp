using System.Reflection;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies categories whose names differ only by case (which Archidekt allows), the schema upgrade that allows them,
/// and the plain-words constraint errors.
/// </summary>
public sealed class CategoryNameTests
{
    /// <summary>
    /// Verifies two categories of one deck whose names differ only by case are both stored.
    /// </summary>
    [Fact]
    public async Task Create_WithCaseDistinctCategories_StoresBoth()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");

        OperationResult<DeckDocument> created = await store.CreateAsync(
            new DeckCreateRequest(
                "Cases",
                Categories: [new DeckCategoryDraft("Sideboard"), new DeckCategoryDraft("SIDEBOARD")]),
            TestContext.Current.CancellationToken);

        DeckDocument deck = Assert.IsType<OperationSuccess<DeckDocument>>(created.Value).Data;
        Assert.Equal(["SIDEBOARD", "Sideboard"], deck.Categories.Select(category => category.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies an exact repeat of a name is still refused, and the message names the cause.
    /// </summary>
    [Fact]
    public async Task Create_WithTheSameNameTwice_NamesTheCause()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");

        OperationResult<DeckDocument> created = await store.CreateAsync(
            new DeckCreateRequest(
                "Repeat",
                Categories: [new DeckCategoryDraft("Ramp"), new DeckCategoryDraft("Ramp")]),
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationInvalidInput>(created.Value);
    }

    /// <summary>
    /// Verifies the constraint descriptions name the table and cause instead of one generic sentence.
    /// </summary>
    [Theory]
    [InlineData("UNIQUE constraint failed: deck_categories.deck_id, deck_categories.name", "same name")]
    [InlineData("UNIQUE constraint failed: deck_entries.entry_id", "entry ID is already used")]
    [InlineData("UNIQUE constraint failed: provider_bindings.deck_id", "binding")]
    [InlineData("UNIQUE constraint failed: deck_entry_categories.entry_id", "primary category")]
    [InlineData("CHECK constraint failed: quantity > 0", "quantity > 0")]
    [InlineData("constraint", "violates a local constraint")]
    public void DescribeConstraint_NamesTheCause(string sqliteMessage, string expected)
    {
        string text = SqliteDeckStore.DescribeConstraint(new SqliteException(sqliteMessage, 19));

        Assert.Contains(expected, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a database made with schema 1 is upgraded on the next write: decks and category links are kept, and
    /// case-distinct names then work.
    /// </summary>
    [Fact]
    public async Task VersionOneDatabase_IsUpgradedAndKeepsItsData()
    {
        using TemporaryDeckDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "decks.db");
        MakeVersionOneDatabase(path);
        using SqliteDeckStore store = new(temporary.Path, "test");
        Guid deckId = Guid.Parse("00000000-0000-4000-8000-000000000001");

        DeckDocument before = Assert.IsType<OperationSuccess<DeckDocument>>(
            (await store.GetAsync(deckId, TestContext.Current.CancellationToken)).Value).Data;
        OperationResult<DeckDocument> created = await store.CreateAsync(
            new DeckCreateRequest("New", Categories: [new DeckCategoryDraft("A"), new DeckCategoryDraft("a")]),
            TestContext.Current.CancellationToken);
        DeckDocument after = Assert.IsType<OperationSuccess<DeckDocument>>(
            (await store.GetAsync(deckId, TestContext.Current.CancellationToken)).Value).Data;

        Assert.Single(before.CategoryAssignments);
        Assert.IsType<OperationSuccess<DeckDocument>>(created.Value);
        Assert.Equal(before.Categories.Single().CategoryId, after.Categories.Single().CategoryId);
        Assert.Equal(before.CategoryAssignments, after.CategoryAssignments);
        Assert.Equal(2, await ReadVersionAsync(path));
    }

    /// <summary>
    /// Reads the highest applied schema version of a database file.
    /// </summary>
    private static async Task<int> ReadVersionAsync(string path)
    {
        using SqliteConnection connection = new($"Data Source={path};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds a database exactly as schema 1 did, with one deck, one entry, and one category link.
    /// </summary>
    private static void MakeVersionOneDatabase(string path)
    {
        Assembly assembly = typeof(SqliteDeckStore).Assembly;
        Type database = assembly.GetType("MtgMcp.Decks.DeckDatabase")!;
        string sql = (string)database.GetField("SchemaV1Sql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        object migration = database.GetField("SchemaV1Migration", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        string checksum = (string)migration.GetType()
            .GetProperty("Checksum", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(migration)!;
        using SqliteConnection connection = new($"Data Source={path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql + $"""

            INSERT INTO schema_migrations VALUES (1, '2026-10-01T00:00:00.0000000+00:00', 'test', '{checksum}');
            INSERT INTO decks VALUES ('00000000-0000-4000-8000-000000000001', 'Old', '', 'commander', 1,
                '2026-10-01T00:00:00.0000000+00:00', '2026-10-01T00:00:00.0000000+00:00');
            INSERT INTO deck_entries VALUES ('00000000-0000-4000-8000-000000000011',
                '00000000-0000-4000-8000-000000000001', 1, 'Sol Ring', NULL, NULL, NULL, NULL, 'en', 'nonfoil',
                'main', 0);
            INSERT INTO deck_categories VALUES ('00000000-0000-4000-8000-000000000021',
                '00000000-0000-4000-8000-000000000001', 'Ramp', NULL, 0);
            INSERT INTO deck_entry_categories VALUES ('00000000-0000-4000-8000-000000000011',
                '00000000-0000-4000-8000-000000000021', 1);
            """;
        command.ExecuteNonQuery();
    }
}
