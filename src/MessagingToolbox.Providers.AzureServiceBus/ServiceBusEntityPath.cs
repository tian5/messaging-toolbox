namespace MessagingToolbox.Providers.AzureServiceBus;

/// <summary>Splits entity paths: subscriptions are addressed as "{topic}/subscriptions/{name}".</summary>
internal static class ServiceBusEntityPath
{
    private const string SubscriptionPathMarker = "/subscriptions/";

    public static (string Path, string? Subscription) Split(string entityPath)
    {
        int index = entityPath.IndexOf(SubscriptionPathMarker, StringComparison.Ordinal);
        return index < 0
            ? (entityPath, null)
            : (entityPath[..index], entityPath[(index + SubscriptionPathMarker.Length)..]);
    }
}
