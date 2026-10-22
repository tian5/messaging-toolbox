using System.Diagnostics;
using MessagingToolbox.Abstractions;

namespace MessagingToolbox.Providers.RabbitMQ;

/// <summary>
/// Shared activity source for provider operations (peek/send/purge), so the Aspire dashboard
/// shows toolbox telemetry next to the app's own traces (architecture doc §9).
/// </summary>
internal static class Telemetry
{
    public static readonly ActivitySource Source = new(TelemetryConstants.ActivitySourceName);
}
