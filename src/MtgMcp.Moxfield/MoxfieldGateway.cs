using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Moxfield;

/// <summary>
/// Imports Moxfield decks through Moxfield's anonymous deck API.
/// </summary>
public sealed partial class MoxfieldGateway : IMoxfieldGateway
{
    /// <summary>
    /// Sends Moxfield API requests for this gateway instance.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Holds configured Moxfield endpoint settings.
    /// </summary>
    private readonly MoxfieldOptions options;

    /// <summary>
    /// Creates a gateway that sends JSON requests to Moxfield.
    /// </summary>
    public MoxfieldGateway(HttpClient httpClient, IOptions<MoxfieldOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= this.options.BaseAddress;
        this.httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );

        MtgMcpHttpDefaults.ApplyUserAgent(this.httpClient, this.options.UserAgent);
    }

    /// <summary>
    /// Imports a public or unlisted Moxfield deck as a local workspace.
    /// </summary>
    public async Task<DeckWorkspace> ImportDeckAsync(
        string deckIdOrUrl,
        CancellationToken cancellationToken
    )
    {
        string deckId = ExtractDeckId(deckIdOrUrl);
        using JsonDocument document = await GetJsonAsync(
                $"v3/decks/all/{Uri.EscapeDataString(deckId)}",
                cancellationToken
            )
            .ConfigureAwait(false);

        return ParseDeck(document.RootElement, deckId, ToDeckUrl(deckId));
    }

    /// <summary>
    /// Gets JSON from Moxfield and reports common anonymous API failures clearly.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(
        string uri,
        CancellationToken cancellationToken
    )
    {
        using HttpResponseMessage response = await httpClient
            .GetAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        string responseBody = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return string.IsNullOrWhiteSpace(responseBody)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(responseBody);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden
            && options.EnableCurlFallback
            && await TryGetJsonWithCurlAsync(uri, cancellationToken).ConfigureAwait(false)
                is JsonDocument curlDocument)
        {
            return curlDocument;
        }

        throw CreateRequestException(response, responseBody);
    }

    /// <summary>
    /// Retries through curl for Moxfield edges that block .NET's HTTP fingerprint.
    /// </summary>
    private async Task<JsonDocument?> TryGetJsonWithCurlAsync(
        string uri,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(options.CurlPath)
            || !TryBuildAbsoluteUri(uri, out Uri? absoluteUri))
        {
            return null;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = options.CurlPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--fail-with-body");
        startInfo.ArgumentList.Add("--silent");
        startInfo.ArgumentList.Add("--show-error");
        startInfo.ArgumentList.Add("--location");
        startInfo.ArgumentList.Add("--max-time");
        startInfo.ArgumentList.Add("30");
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(options.UserAgent)
            ? MtgMcpHttpDefaults.UserAgent
            : options.UserAgent);
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Accept: application/json");
        startInfo.ArgumentList.Add(absoluteUri!.ToString());

        try
        {
            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            return JsonDocument.Parse(output);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or Win32Exception
                or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves relative API paths against the configured Moxfield base address.
    /// </summary>
    private bool TryBuildAbsoluteUri(string uri, out Uri? absoluteUri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out absoluteUri))
        {
            return true;
        }

        Uri baseAddress = httpClient.BaseAddress ?? options.BaseAddress;
        return Uri.TryCreate(baseAddress, uri, out absoluteUri);
    }

    /// <summary>
    /// Creates a sanitized Moxfield HTTP exception.
    /// </summary>
    private static HttpRequestException CreateRequestException(
        HttpResponseMessage response,
        string responseBody
    )
    {
        string? hint = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Moxfield may have blocked anonymous API access.",
            HttpStatusCode.TooManyRequests => "Moxfield rate-limited anonymous API access.",
            HttpStatusCode.NotFound => "The deck may be private, deleted, or mistyped.",
            _ => null,
        };
        return MtgMcpHttpRetry.CreateRequestException("Moxfield", response, responseBody, hint);
    }
}
