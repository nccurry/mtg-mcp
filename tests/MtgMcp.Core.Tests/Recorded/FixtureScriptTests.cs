using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MtgMcp.Core.Tests.Recorded;

/// <summary>
/// Verifies the pure helpers of the fixture refresh script by running them in pwsh on inline JSON, with no network.
/// </summary>
public sealed class FixtureScriptTests
{
    /// <summary>
    /// Holds an Archidekt-like deck with an owner, a folder, deck text, two cards, and two categories.
    /// </summary>
    private const string Deck = """
        {"description":"migrated from Workspace","parentFolder":77,
         "owner":{"id":5,"username":"someone","avatar":"a.png","frame":"f"},
         "cards":[
           {"card":{"oracleCard":{"name":"Sol Ring"}},"categories":["Ramp"]},
           {"card":{"oracleCard":{"name":"Other"}},"categories":["Junk"]}],
         "categories":[{"name":"Ramp"},{"name":"Junk"}]}
        """;

    /// <summary>
    /// Verifies personal and price fields are stripped at any depth.
    /// </summary>
    [Fact]
    public void ConvertToStableJson_PersonalAndPriceFields_AreStrippedAtAnyDepth()
    {
        const string json = """
            {"username":"u","card":{"prices":{"usd":"1.00","eur":null},"image_uris":{"small":"x"},
             "list":[{"avatar":"a","keep":1,"purchase_uris":{"x":"y"}}]}}
            """;

        JsonNode result = JsonNode.Parse(Stable(json, "$null"))!;

        Assert.Null(result["username"]);
        Assert.Null(result["card"]!["image_uris"]);
        Assert.Null(result["card"]!["prices"]!["usd"]);
        Assert.Null(result["card"]!["list"]![0]!["avatar"]);
        Assert.Null(result["card"]!["list"]![0]!["purchase_uris"]);
        Assert.Equal(1, result["card"]!["list"]![0]!["keep"]!.GetValue<int>());
    }

    /// <summary>
    /// Verifies the output uses LF line endings with one trailing newline and a second pass changes nothing.
    /// </summary>
    [Fact]
    public void ConvertToStableJson_AnyJson_IsLfWithTrailingNewlineAndStableOnSecondPass()
    {
        string once = Stable("{\"b\":[1,2],\"a\":{\"c\":null}}", "$null");
        string twice = Stable(once, "$null");

        Assert.EndsWith("}\n", once, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', once);
        Assert.Equal(once, twice);
    }

    /// <summary>
    /// Verifies the read-only guard refuses every method but GET and the one allowed lookup POST.
    /// </summary>
    [Theory]
    [InlineData("GET", "/cards/named", "allowed")]
    [InlineData("POST", "/cards/collection", "allowed")]
    [InlineData("POST", "/cards/search", "refused")]
    [InlineData("DELETE", "/cards/collection", "refused")]
    [InlineData("PUT", "/cards/collection", "refused")]
    public void AssertReadOnlyRequest_Method_AllowsOnlyReads(string method, string path, string expected)
    {
        string script = $$"""
            try { Assert-ReadOnlyRequest -Method '{{method}}' -Path '{{path}}'; [Console]::Out.Write('allowed') }
            catch { [Console]::Out.Write('refused') }
            """;

        Assert.Equal(expected, RunFunctions(script));
    }

    /// <summary>
    /// Verifies a deck response loses the owner id, folder id, deck text, and frame even with no card list to keep.
    /// </summary>
    [Fact]
    public void ConvertToStableJson_DeckWithOwner_ZeroesOwnerAndFolderAndEmptiesDescription()
    {
        JsonNode result = JsonNode.Parse(Stable(Deck, "$null"))!;

        Assert.Equal(0, result["owner"]!["id"]!.GetValue<int>());
        Assert.Equal(0, result["parentFolder"]!.GetValue<int>());
        Assert.Equal(string.Empty, result["description"]!.GetValue<string>());
        Assert.Null(result["owner"]!["frame"]);
        Assert.Null(result["owner"]!["username"]);
        Assert.Equal(2, result["cards"]!.AsArray().Count);
    }

    /// <summary>
    /// Verifies keeping card names trims the cards but leaves every category.
    /// </summary>
    [Fact]
    public void ConvertToStableJson_KeepCardNames_TrimsCardsAndKeepsCategories()
    {
        JsonNode result = JsonNode.Parse(Stable(Deck, "[pscustomobject]@{ keepCardNames = @('Sol Ring') }"))!;

        Assert.Equal(
            "Sol Ring",
            Assert.Single(result["cards"]!.AsArray())!["card"]!["oracleCard"]!["name"]!.GetValue<string>());
        Assert.Equal(2, result["categories"]!.AsArray().Count);
        Assert.Equal(0, result["owner"]!["id"]!.GetValue<int>());
    }

    /// <summary>
    /// Runs ConvertTo-StableJson on the text with the given entry expression and returns its output.
    /// </summary>
    private static string Stable(string json, string entry)
    {
        string script = $$"""
            $entry = {{entry}}
            $once = ConvertTo-StableJson -Text @'
            {{json}}
            '@ -Entry $entry
            [Console]::Out.Write($once)
            """;

        return RunFunctions(script);
    }

    /// <summary>
    /// Runs a pwsh script after dot-sourcing the script helpers and returns its standard output.
    /// </summary>
    private static string RunFunctions(string body)
    {
        string functions = Path.Combine(RecordedManifest.RepositoryRoot(), "scripts", "fixtures.functions.ps1");
        string file = Path.Combine(Path.GetTempPath(), $"fixture-script-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(file, $". '{functions}'{Environment.NewLine}{body}");
        try
        {
            using Process process = Process.Start(new ProcessStartInfo("pwsh")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-File", file },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            })!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
            return output;
        }
        finally
        {
            File.Delete(file);
        }
    }
}
