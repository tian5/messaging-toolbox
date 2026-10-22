namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// A RabbitMQ connection string split into its parts. The string is an AMQP URI
/// (<c>amqp[s]://user:password@host[:port][/vhost]</c>), optionally followed by
/// <c>;Management=http[s]://host:port</c> for brokers whose management API isn't on the AMQP host at
/// port 15672 (15671 with TLS). A host-supplied management endpoint (Aspire) wins over both.
/// </summary>
internal sealed record RabbitMqConnectionSettings(Uri AmqpUri, Uri ManagementUri, string VirtualHost, string UserName, string Password)
{
    private const string ManagementKey = ";Management=";
    private const int DefaultManagementPort = 15672;
    private const int DefaultManagementTlsPort = 15671;

    public static RabbitMqConnectionSettings Parse(string connectionString, string? hostManagementEndpoint)
    {
        string amqp = connectionString.Trim();
        string? management = null;

        int managementIndex = amqp.IndexOf(ManagementKey, StringComparison.OrdinalIgnoreCase);
        if (managementIndex >= 0)
        {
            management = amqp[(managementIndex + ManagementKey.Length)..].Trim();
            amqp = amqp[..managementIndex].Trim();
        }

        if (!Uri.TryCreate(amqp, UriKind.Absolute, out Uri? amqpUri) || amqpUri.Scheme is not ("amqp" or "amqps"))
        {
            throw new FormatException("A RabbitMQ connection string is an AMQP URI: amqp://user:password@host:5672/vhost.");
        }

        (string userName, string password) = Credentials(amqpUri);
        Uri managementUri = new(EnsureTrailingSlash(hostManagementEndpoint ?? management ?? DefaultManagementUrl(amqpUri)));

        return new RabbitMqConnectionSettings(amqpUri, managementUri, VirtualHostOf(amqpUri), userName, password);
    }

    // Per the AMQP URI spec the path is the vhost, URI-escaped; an empty path means the default vhost "/".
    private static string VirtualHostOf(Uri uri)
    {
        string path = uri.AbsolutePath.TrimStart('/');
        return path.Length == 0 ? "/" : Uri.UnescapeDataString(path);
    }

    private static (string UserName, string Password) Credentials(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return ("guest", "guest");
        }

        string[] parts = uri.UserInfo.Split(':', 2);
        return (Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
    }

    private static string DefaultManagementUrl(Uri amqpUri) => amqpUri.Scheme == "amqps"
        ? $"https://{amqpUri.Host}:{DefaultManagementTlsPort}/"
        : $"http://{amqpUri.Host}:{DefaultManagementPort}/";

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
