using System.Text.RegularExpressions;

namespace MessagingToolbox.Providers.AzureServiceBus.Emulator;

/// <summary>
/// Derives the Azure Service Bus emulator admin connection string from a runtime connection string.
/// The emulator exposes <see cref="ServiceBusAdministrationClient"/> on port <c>5300</c> while the
/// AMQP data plane stays on the default port.
/// </summary>
public static partial class EmulatorAdminConnectionString
{
    // Endpoint=sb://host[:port];...  —  capture host and optional port separately.
    [GeneratedRegex(@"Endpoint=sb://(?<host>[^:;]+)(:(?<port>\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EndpointRegex();

    /// <summary>
    /// Returns a connection string identical to <paramref name="connectionString"/> except the
    /// endpoint is rewritten to the admin endpoint: <paramref name="adminEndpoint"/> when the host
    /// supplies one (Aspire maps container port 5300 to a dynamic host port, so the AppHost passes
    /// the resolved address), otherwise <c>sb://host:5300</c>. Returns <see langword="null"/> when
    /// the input does not contain an <c>Endpoint</c> segment.
    /// </summary>
    /// <param name="adminEndpoint">An absolute URI (<c>http://localhost:51234</c>) or <c>host:port</c>.</param>
    public static string? FromRuntimeConnectionString(string connectionString, string? adminEndpoint = null)
    {
        Match match = EndpointRegex().Match(connectionString);
        if (!match.Success)
        {
            return null;
        }

        string authority = ParseAuthority(adminEndpoint) ?? $"{match.Groups["host"].Value}:5300";
        string replacement = $"Endpoint=sb://{authority}";

        return EndpointRegex().Replace(connectionString, replacement, 1);
    }

    private static string? ParseAuthority(string? adminEndpoint)
    {
        if (string.IsNullOrWhiteSpace(adminEndpoint))
        {
            return null;
        }

        string candidate = adminEndpoint.Contains("://", StringComparison.Ordinal) ? adminEndpoint.Trim() : $"http://{adminEndpoint.Trim()}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Host)
            ? $"{uri.Host}:{uri.Port}"
            : null;
    }
}
