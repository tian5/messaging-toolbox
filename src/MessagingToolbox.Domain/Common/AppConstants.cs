namespace MessagingToolbox.Domain.Common;

/// <summary>Magic strings live here, not in attributes or code (code style conventions §5).</summary>
public static class AppConstants
{
    /// <summary>Configuration connection-string name for the toolbox SQLite database (Aspire injects it).</summary>
    public const string ToolboxDatabaseConnectionName = "toolboxdb";

    /// <summary>Standalone fallback database file when no connection string is configured.</summary>
    public const string ToolboxDatabaseFileName = "messagingtoolbox.db";

    /// <summary>Default directory for writable tool state (SQLite file, Data Protection keys).</summary>
    public const string ToolboxDataDirectoryName = "data";

    /// <summary>Folder canned messages land in until the user organizes them.</summary>
    public const string DefaultCannedMessageFolder = "General";
}
