namespace MessagingToolbox.Providers.AzureServiceBus.Emulator;

/// <summary>
/// Detects emulator connections (FR-1.2) so capabilities can be downgraded before any call is
/// made. Covers the documented UseDevelopmentEmulator=true connection-string flag; Aspire
/// emulator resources resolve to exactly such a connection string at runtime.
/// </summary>
public static class EmulatorDetection
{
    private const string EmulatorFlagKey = "UseDevelopmentEmulator";

    /// <summary>
    /// True when the connection string targets the Azure Service Bus emulator. Parsed per
    /// key/value pair (whitespace and case tolerant) rather than by substring, so
    /// "UseDevelopmentEmulator = true" is recognized and similarly named keys are not.
    /// </summary>
    public static bool IsEmulatorConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        foreach (string pair in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            if (pair[..separator].Trim().Equals(EmulatorFlagKey, StringComparison.OrdinalIgnoreCase))
            {
                return bool.TryParse(pair[(separator + 1)..].Trim(), out bool enabled) && enabled;
            }
        }

        return false;
    }
}
