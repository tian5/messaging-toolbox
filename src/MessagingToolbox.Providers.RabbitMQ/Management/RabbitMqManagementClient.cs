using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>
/// Thin typed client over the management HTTP API (no SDK exists from the RabbitMQ team, and the
/// toolbox needs a handful of endpoints). One instance per connection, owned by its provider.
/// </summary>
public sealed class RabbitMqManagementClient : IRabbitMqManagementClient
{
    private const string QueueColumns = "name,type,messages,messages_ready,messages_unacknowledged,message_bytes,state,durable,auto_delete,exclusive,consumers,policy,arguments,effective_policy_definition";
    private const string ExchangeColumns = "name,type,internal,durable,auto_delete,arguments";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly string _virtualHost;

    public RabbitMqManagementClient(Uri managementUri, string virtualHost, string userName, string password)
        : this(new HttpClient(), managementUri, virtualHost, userName, password)
    {
    }

    internal RabbitMqManagementClient(HttpClient httpClient, Uri managementUri, string virtualHost, string userName, string password)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = managementUri;
        _httpClient.Timeout = RequestTimeout;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));
        _virtualHost = Uri.EscapeDataString(virtualHost);
    }

    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync($"api/vhosts/{_virtualHost}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<RabbitMqQueueInfo>> ListQueuesAsync(CancellationToken cancellationToken)
        => await GetAsync<List<RabbitMqQueueInfo>>($"api/queues/{_virtualHost}?columns={QueueColumns}", cancellationToken);

    public async Task<RabbitMqQueueInfo> GetQueueAsync(string name, CancellationToken cancellationToken)
        => await GetAsync<RabbitMqQueueInfo>(QueueUrl(name), cancellationToken);

    public async Task<IReadOnlyList<RabbitMqExchangeInfo>> ListExchangesAsync(CancellationToken cancellationToken)
        => await GetAsync<List<RabbitMqExchangeInfo>>($"api/exchanges/{_virtualHost}?columns={ExchangeColumns}", cancellationToken);

    public async Task<RabbitMqExchangeInfo> GetExchangeAsync(string name, CancellationToken cancellationToken)
        => await GetAsync<RabbitMqExchangeInfo>(ExchangeUrl(name), cancellationToken);

    public async Task<IReadOnlyList<RabbitMqBindingInfo>> ListBindingsAsync(CancellationToken cancellationToken)
        => await GetAsync<List<RabbitMqBindingInfo>>($"api/bindings/{_virtualHost}", cancellationToken);

    public async Task DeclareQueueAsync(string name, RabbitMqQueueDeclaration declaration, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(QueueUrl(name), declaration, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteQueueAsync(string name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(QueueUrl(name), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeclareExchangeAsync(string name, RabbitMqExchangeDeclaration declaration, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(ExchangeUrl(name), declaration, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteExchangeAsync(string name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(ExchangeUrl(name), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<string> CreateBindingAsync(
        string source,
        string destination,
        bool destinationIsQueue,
        RabbitMqBindingDeclaration declaration,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(BindingsUrl(source, destination, destinationIsQueue), declaration, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        // The API answers 201 with the new binding's URL, whose last segment is its properties key.
        return response.Headers.Location is { } location
            ? Uri.UnescapeDataString(location.OriginalString.TrimEnd('/').Split('/')[^1])
            : string.Empty;
    }

    public async Task DeleteBindingAsync(string source, string destination, bool destinationIsQueue, string propertiesKey, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(
            $"{BindingsUrl(source, destination, destinationIsQueue)}/{Uri.EscapeDataString(propertiesKey)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private string BindingsUrl(string source, string destination, bool destinationIsQueue)
        => $"api/bindings/{_virtualHost}/e/{Uri.EscapeDataString(source)}/{(destinationIsQueue ? "q" : "e")}/{Uri.EscapeDataString(destination)}";

    private string QueueUrl(string name) => $"api/queues/{_virtualHost}/{Uri.EscapeDataString(name)}";

    private string ExchangeUrl(string name) => $"api/exchanges/{_virtualHost}/{Uri.EscapeDataString(name)}";

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException($"The management API returned an empty response for '{relativeUrl}'.");
    }

    // The API explains refusals in {"error": …, "reason": …}; keep the reason, it's what the user needs
    // (e.g. "inequivalent arg 'x-queue-type' for queue 'orders'").
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? reason = null;
        try
        {
            RabbitMqErrorResponse? error = await response.Content.ReadFromJsonAsync<RabbitMqErrorResponse>(cancellationToken);
            reason = error?.Reason ?? error?.Error;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Not the API's JSON error shape (e.g. a proxy's HTML page); the status code still tells.
        }

        throw new HttpRequestException(
            reason ?? $"The management API answered {(int)response.StatusCode} {response.ReasonPhrase}.",
            inner: null,
            response.StatusCode);
    }
}
