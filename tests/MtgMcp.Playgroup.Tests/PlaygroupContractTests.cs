using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Proves the pinned contract inventory, configuration bounds, and lossless evidence models.
/// </summary>
public sealed class PlaygroupContractTests
{
    /// <summary>Lists the exact public operation IDs registered by this child.</summary>
    private static readonly string[] ExpectedOperationIds =
    [
        "batchImportEvents",
        "createLiveSession",
        "getCommanderById",
        "getCommanderByName",
        "getCommandersTurnDamage",
        "getCurrentUser",
        "getDeckById",
        "getDeckEloHistory",
        "getPlaygroupGame",
        "getUserById",
        "getUserPlaygroup",
        "listPlaygroupGames",
        "listPlaygroupMembers",
        "listUserDecks",
        "listUserPlaygroups",
    ];

    /// <summary>Verifies exact bytes, version, authentication scheme, and operation inventory.</summary>
    [Fact]
    public void OpenApiFixture_MatchesPinnedPublicContract()
    {
        string path = Path.Combine(FindRepositoryRoot(), "src", "MtgMcp.Playgroup", "Fixtures", "OpenApi", "public-v1-1.0.0.yaml");
        byte[] bytes = File.ReadAllBytes(path);
        string text = Encoding.UTF8.GetString(bytes);

        Assert.Equal(41_646, bytes.Length);
        Assert.Equal(
            PlaygroupContract.OpenApiChecksum,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Contains("openapi: 3.1.0", text, StringComparison.Ordinal);
        Assert.Contains("version: 1.0.0", text, StringComparison.Ordinal);
        Assert.Contains("scheme: bearer", text, StringComparison.Ordinal);
        string[] operationIds = Regex.Matches(text, @"(?m)^\s+operationId:\s+(\w+)\s*$")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedOperationIds, operationIds);
    }

    /// <summary>Verifies exact contract helpers accept valid input and reject malformed input.</summary>
    [Fact]
    public void ContractHelpers_ValidateAndPreserveProviderData()
    {
        Assert.Equal(7, PlaygroupContract.PositiveId(7, "id"));
        Assert.Equal("Atraxa", PlaygroupContract.Required(" Atraxa ", "name"));
        Assert.Equal(
            "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
            PlaygroupContract.Checksum("{}"));
        JsonElement parsed = PlaygroupContract.ParseData("{\"known\":null,\"extension\":42}");
        Assert.Equal(JsonValueKind.Null, parsed.GetProperty("known").ValueKind);
        Assert.Equal(42, parsed.GetProperty("extension").GetInt32());

        Assert.Equal("invalid-provider-id", Assert.Throws<PlaygroupProviderException>(
            () => PlaygroupContract.PositiveId(0, "id")).ReasonCode);
        Assert.Equal("invalid-provider-input", Assert.Throws<PlaygroupProviderException>(
            () => PlaygroupContract.Required(" ", "name")).ReasonCode);
        Assert.Equal("provider-contract-unsupported", Assert.Throws<PlaygroupProviderException>(
            () => PlaygroupContract.ParseData("not-json")).ReasonCode);
    }

    /// <summary>Verifies options enforce the conservative minimum and bounded Retry-After policy.</summary>
    [Fact]
    public void Options_ValidateSupportedBounds()
    {
        PlaygroupOptions defaults = PlaygroupOptions.CreateDefault(" key ");
        Assert.Equal("key", defaults.ApiKey);
        Assert.Equal(TimeSpan.FromMilliseconds(250), defaults.MinimumRequestInterval);
        defaults.Validate();
        Assert.Null(PlaygroupOptions.CreateDefault(" ").ApiKey);

        Assert.Throws<ArgumentException>(() => new PlaygroupOptions(
            " ",
            null,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(1)).Validate());
        Assert.Throws<ArgumentException>(() => (defaults with { CredentialsFile = " " }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with
        {
            MinimumRequestInterval = TimeSpan.FromMilliseconds(249),
        }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with
        {
            MaximumRetryAfter = TimeSpan.Zero,
        }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (defaults with
        {
            MaximumRetryAfter = TimeSpan.FromMinutes(6),
        }).Validate());
    }

    /// <summary>Verifies evidence and capability collections detach from mutable caller state.</summary>
    [Fact]
    public void Models_DefensivelyCopyCollectionsAndJson()
    {
        string[] limitations = ["first"];
        using JsonDocument document = JsonDocument.Parse("{\"extension\":true}");
        PlaygroupEvidence evidence = new(
            "operation",
            "GET /path",
            "1.0.0",
            PlaygroupContract.OpenApiChecksum,
            DateTimeOffset.UtcNow,
            PlaygroupContract.Checksum("{}"),
            limitations,
            document.RootElement);
        limitations[0] = "changed";
        Assert.Equal("first", Assert.Single(evidence.Limitations));
        Assert.True(evidence.Data.GetProperty("extension").GetBoolean());
    }

    /// <summary>Finds the repository root from the test output directory.</summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mtg-mcp repository root.");
    }
}
