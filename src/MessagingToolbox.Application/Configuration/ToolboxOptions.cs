namespace MessagingToolbox.Application.Configuration;

/// <summary>
/// Toolbox settings, bound from the "ToolboxOptions" configuration section (nameof convention).
/// Container hosts override everything via environment variables, e.g.
/// ToolboxOptions__Connections__0__Name.
/// </summary>
public sealed class ToolboxOptions
{
    /// <summary>Connections seeded from configuration (Aspire injection or standalone env vars).</summary>
    public List<PreconfiguredConnectionOptions> Connections { get; set; } = [];

    /// <summary>Tree/count auto-refresh interval in seconds; 0 disables polling. Auto-refresh is opt-in — the manual Refresh button is always available.</summary>
    public int RefreshIntervalSeconds { get; set; } = 0;

    /// <summary>How often (seconds, min 1) the live watch peeks the entities the user pinned for new arrivals.</summary>
    public int WatchIntervalSeconds { get; set; } = 3;

    /// <summary>Directory for writable tool state (SQLite file, Data Protection keys); a mounted volume in the container.</summary>
    public string DataDirectory { get; set; } = "data";
}
