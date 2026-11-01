using System.Text.Json;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the refusal of a wrongly typed value inside a later item of a <c>local_deck_apply_changes</c> change list.
/// </summary>
public sealed class ChangeListArgumentErrorTests : IClassFixture<ToolCatalogFixture>
{
    /// <summary>
    /// Holds the argument catalog of one full registration.
    /// </summary>
    private readonly ToolArgumentCatalog catalog;

    /// <summary>
    /// Takes the catalog the fixture built.
    /// </summary>
    public ChangeListArgumentErrorTests(ToolCatalogFixture fixture)
    {
        catalog = fixture.Catalog;
    }

    /// <summary>
    /// Verifies a fractional sort order names the full path of the field in the fourth change and the plain expected type.
    /// </summary>
    [Theory]
    [InlineData("update-entry", "entry", "changes[3].entry.sortOrder")]
    [InlineData("add-entry", "entryDraft", "changes[3].entryDraft.sortOrder")]
    public void SortOrder_Fractional_NamesTheFullPathAndPlainType(string kind, string property, string path)
    {
        OperationInvalidInput failure = Diagnose(ChangeAt3(Change(kind, property, "1.5")));

        Assert.Equal("invalid-arguments", failure.ReasonCode);
        Assert.Equal(path, failure.Path);
        Assert.Equal(
            "The value at '" + path + "' must be an integer.",
            failure.Message);
    }

    /// <summary>
    /// Verifies a sort order written as the JSON string "2" is accepted today rather than refused.
    /// </summary>
    [Theory]
    [InlineData("update-entry", "entry")]
    [InlineData("add-entry", "entryDraft")]
    public void SortOrder_NumberInAString_IsAccepted(string kind, string property)
    {
        Dictionary<string, JsonElement> arguments = ChangeAt3(Change(kind, property, "\"2\""));

        Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));
    }

    /// <summary>
    /// Verifies an entry identifier that is not a UUID names the full path of the field in the fourth change and "a UUID".
    /// </summary>
    [Theory]
    [InlineData("{ \"kind\": \"remove-entry\", \"entryId\": \"not-a-guid\" }", "changes[3].entryId")]
    [InlineData("{ \"kind\": \"assign-category\", \"entryId\": \"not-a-guid\", \"categoryId\": \"0193e97a-ef95-7700-8000-000000000002\", \"isPrimary\": true }", "changes[3].entryId")]
    [InlineData("{ \"kind\": \"update-entry\", \"entry\": { \"entryId\": \"not-a-guid\", \"quantity\": 1, \"cardName\": \"Sol Ring\", \"zone\": \"mainboard\" } }", "changes[3].entry.entryId")]
    public void EntryId_MalformedGuid_NamesTheFullPathAndPlainType(string change, string path)
    {
        OperationInvalidInput failure = Diagnose(ChangeAt3(change));

        Assert.Equal("invalid-arguments", failure.ReasonCode);
        Assert.Equal(path, failure.Path);
        Assert.Equal(
            "The value at '" + path + "' must be a UUID.",
            failure.Message);
    }

    /// <summary>
    /// Verifies a boolean field given text names its full path and "true or false".
    /// </summary>
    [Fact]
    public void IsPrimary_Text_NamesTheFullPathAndBoolean()
    {
        OperationInvalidInput failure = Diagnose(ChangeAt3(
            "{ \"kind\": \"assign-category\", \"entryId\": \"0193e97a-ef95-7700-8000-000000000003\", \"categoryId\": \"0193e97a-ef95-7700-8000-000000000002\", \"isPrimary\": \"yes\" }"));

        Assert.Equal("changes[3].isPrimary", failure.Path);
        Assert.Equal("The value at 'changes[3].isPrimary' must be true or false.", failure.Message);
    }

    /// <summary>
    /// Verifies a text field given a number names its full path and "text".
    /// </summary>
    [Fact]
    public void CardName_Number_NamesTheFullPathAndText()
    {
        OperationInvalidInput failure = Diagnose(ChangeAt3(
            "{ \"kind\": \"add-entry\", \"entryDraft\": { \"quantity\": 1, \"cardName\": 5 } }"));

        Assert.Equal("changes[3].entryDraft.cardName", failure.Path);
        Assert.Equal("The value at 'changes[3].entryDraft.cardName' must be text.", failure.Message);
    }

    /// <summary>
    /// Verifies a list argument given a scalar names its path and "a list".
    /// </summary>
    [Fact]
    public void Changes_Scalar_NamesTheListPath()
    {
        Dictionary<string, JsonElement> arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            "{ \"deckId\": \"0193e97a-ef95-7700-8000-000000000001\", \"expectedRevision\": 1, \"changes\": 7 }")!;

        OperationInvalidInput failure = Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));

        Assert.Equal("changes", failure.Path);
        Assert.Equal("The value at 'changes' must be a list.", failure.Message);
    }

    /// <summary>
    /// Verifies a list item given a scalar instead of an object names the item path and "an object".
    /// </summary>
    [Fact]
    public void ChangeItem_Scalar_NamesTheItemPath()
    {
        OperationInvalidInput failure = Diagnose(ChangeAt3("7"));

        Assert.Equal("changes[3]", failure.Path);
        Assert.Equal("The value at 'changes[3]' must be an object.", failure.Message);
    }

    /// <summary>
    /// Builds one add-entry or update-entry change with the given sort order written as JSON text.
    /// </summary>
    private static string Change(string kind, string property, string sortOrder)
    {
        string draft = kind == "update-entry"
            ? "{ \"entryId\": \"0193e97a-ef95-7700-8000-000000000004\", \"quantity\": 1, \"cardName\": \"Sol Ring\", \"zone\": \"mainboard\", \"sortOrder\": " + sortOrder + " }"
            : "{ \"quantity\": 1, \"cardName\": \"Sol Ring\", \"sortOrder\": " + sortOrder + " }";
        return "{ \"kind\": \"" + kind + "\", \"" + property + "\": " + draft + " }";
    }

    /// <summary>
    /// Builds the argument set whose fourth change is the given JSON, after three valid removals.
    /// </summary>
    private static Dictionary<string, JsonElement> ChangeAt3(string change)
    {
        string valid = "{ \"kind\": \"remove-entry\", \"entryId\": \"0193e97a-ef95-7700-8000-000000000003\" }";
        string json = "{ \"deckId\": \"0193e97a-ef95-7700-8000-000000000001\", \"expectedRevision\": 1, \"changes\": [ " +
            valid + ", " + valid + ", " + valid + ", " + change + " ] }";
        Dictionary<string, JsonElement> arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        WholeNumberArguments.Normalize(arguments);
        return arguments;
    }

    /// <summary>
    /// Runs the shared argument diagnosis for the batch edit tool and returns the refusal.
    /// </summary>
    private OperationInvalidInput Diagnose(Dictionary<string, JsonElement> arguments)
    {
        return Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));
    }
}
