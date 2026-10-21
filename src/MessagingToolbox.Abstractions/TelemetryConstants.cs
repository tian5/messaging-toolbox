namespace MessagingToolbox.Abstractions;

/// <summary>
/// Shared observability constants. Every host and provider traces through the same activity
/// source, so the Aspire dashboard shows MessagingToolbox telemetry next to the app's own traces.
/// </summary>
public static class TelemetryConstants
{
    /// <summary>Name of the <see cref="System.Diagnostics.ActivitySource"/> used around provider operations.</summary>
    public const string ActivitySourceName = "MessagingToolbox";
}
