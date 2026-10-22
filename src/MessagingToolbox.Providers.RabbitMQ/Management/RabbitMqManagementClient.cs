using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace MessagingToolbox.Providers.RabbitMQ.Management;

/// <summary>
/// Thin typed client over the management HTTP API (no SDK exists from the RabbitMQ team, and the
/// toolbox needs a handful of read endpoints). One instance per connection, owned by its provider.
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
        => await GetAsync<RabbitMqQueueInfo>($"api/queues/{_virtualHost}/{Uri.EscapeDataString(name)}", cancellationToken);

    public async Task<IReadOnlyList<RabbitMqExchangeInfo>> ListExchangesAsync(CancellationToken cancellationToken)
        => await GetAsync<List<RabbitMqExchangeInfo>>($"api/exchanges/{_virtualHost}?columns={ExchangeColumns}", cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new HttpRequestException($"The management API returned an empty response for '{relativeUrl}'.");
    }
}
