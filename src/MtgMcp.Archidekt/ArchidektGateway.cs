using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

public sealed partial class ArchidektGateway : IArchidektGateway, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly ArchidektOptions options;
    private readonly SemaphoreSlim authLock = new(1, 1);
    private ArchidektCredentials? credentials;
    private string? credentialsFileError;

    public ArchidektGateway(HttpClient httpClient, IOptions<ArchidektOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= this.options.BaseAddress;
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Dispose()
    {
        authLock.Dispose();
    }
}
