using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Covers shared credentials-file parsing without adapter-specific key policy.
/// </summary>
public sealed class CredentialsFileTests
{
    /// <summary>
    /// Verifies key=value files ignore comments and trim keys and values.
    /// </summary>
    [Fact]
    public async Task Read_ParsesKeyValueCredentials()
    {
        string credentialsFile = CreateTempCredentialsPath();
        await File.WriteAllTextAsync(
            credentialsFile,
            """
            # comment
            apiKey = file-api-key
            accessToken=file-access-token
            """,
            TestContext.Current.CancellationToken);

        try
        {
            IReadOnlyDictionary<string, string> values = ReadPlaygroupStyle(credentialsFile);

            values.Should().Contain("apiKey", "file-api-key");
            values.Should().Contain("accessToken", "file-access-token");
        }
        finally
        {
            File.Delete(credentialsFile);
        }
    }

    /// <summary>
    /// Verifies adapters can opt into raw scalar JSON credentials.
    /// </summary>
    [Fact]
    public async Task Read_AllowsJsonScalarValuesWhenConfigured()
    {
        string credentialsFile = CreateTempCredentialsPath();
        await File.WriteAllTextAsync(
            credentialsFile,
            """{ "apiKey": 12345 }""",
            TestContext.Current.CancellationToken);

        try
        {
            IReadOnlyDictionary<string, string> values = ReadPlaygroupStyle(credentialsFile);

            values["apiKey"].Should().Be("12345");
        }
        finally
        {
            File.Delete(credentialsFile);
        }
    }

    /// <summary>
    /// Verifies adapters can require JSON credential values to be strings.
    /// </summary>
    [Fact]
    public async Task Read_RejectsJsonScalarValuesWhenStringsAreRequired()
    {
        string credentialsFile = CreateTempCredentialsPath();
        await File.WriteAllTextAsync(
            credentialsFile,
            """{ "username": 12345 }""",
            TestContext.Current.CancellationToken);

        try
        {
            Action act = () => ReadArchidektStyle(credentialsFile);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*fields must be strings*");
        }
        finally
        {
            File.Delete(credentialsFile);
        }
    }

    /// <summary>
    /// Verifies malformed JSON errors do not echo the credentials file contents.
    /// </summary>
    [Fact]
    public async Task Read_MalformedJsonErrorDoesNotEchoSecretText()
    {
        string credentialsFile = CreateTempCredentialsPath();
        await File.WriteAllTextAsync(
            credentialsFile,
            """{ "apiKey": "super-secret-token", }""",
            TestContext.Current.CancellationToken);

        try
        {
            Action act = () => ReadPlaygroupStyle(credentialsFile);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*looks like JSON but could not be parsed*")
                .Which.Message.Should().NotContain("super-secret-token");
        }
        finally
        {
            File.Delete(credentialsFile);
        }
    }

    /// <summary>
    /// Creates a unique temp-file path for one credentials parser test.
    /// </summary>
    private static string CreateTempCredentialsPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.credentials");
    }

    /// <summary>
    /// Reads with the tolerant Playgroup credential-file policy.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadPlaygroupStyle(string credentialsFile)
    {
        return MtgMcpCredentialsFile.Read(
            credentialsFile,
            providerName: "Playgroup",
            keyValueExample: "apiKey=value, accessToken=value, or token=value",
            jsonObjectRequirement: "must contain a JSON object or key=value lines.",
            jsonArrayLooksLikeJson: true,
            requireJsonStringValues: false);
    }

    /// <summary>
    /// Reads with the strict Archidekt credential-file policy.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadArchidektStyle(string credentialsFile)
    {
        return MtgMcpCredentialsFile.Read(
            credentialsFile,
            providerName: "Archidekt",
            keyValueExample: "username=value or password=value",
            jsonObjectRequirement: "must contain a JSON object.",
            jsonArrayLooksLikeJson: false,
            requireJsonStringValues: true);
    }
}
