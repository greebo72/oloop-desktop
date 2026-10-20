using System.Text.Json;

namespace OloopApp.Core.Io;

/// <summary>
/// The app settings. Stored as JSON next to the app data, so the data root
/// and the actor survive a restart.
/// </summary>
public sealed class OloopSettings
{
    /// <summary>The data root. Falls back to the OLOOP_ROOT environment variable.</summary>
    public string? DataRoot { get; set; }

    /// <summary>The actor written into createdBy/updatedBy. Falls back to OLOOP_ACTOR, then the OS user.</summary>
    public string Actor { get; set; } = "";

    /// <summary>Whether closed tasks are shown in the list.</summary>
    public bool ShowClosed { get; set; } = true;

    /// <summary>The grouping mode name (None, Created, Modified, Type, Status).</summary>
    public string Grouping { get; set; } = "None";

    /// <summary>The "silent since" filter in days. Null disables it.</summary>
    public double? StaleDays { get; set; }

    /// <summary>
    /// The path of the settings file: %APPDATA%/oloop/settings.json on Windows,
    /// ~/.config/oloop/settings.json elsewhere.
    /// </summary>
    public static string DefaultPath => Path.Combine(AppDataFolder(), "oloop", "settings.json");

    /// <summary>The folder the app keeps its data in.</summary>
    public static string AppDataFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
                return appData;
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
            return xdg;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }

    /// <summary>
    /// The data root: the setting wins, otherwise the OLOOP_ROOT environment
    /// variable.
    /// </summary>
    public string? ResolveDataRoot()
    {
        if (!string.IsNullOrWhiteSpace(DataRoot))
            return DataRoot;

        var fromEnv = Environment.GetEnvironmentVariable("OLOOP_ROOT");
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    /// <summary>The actor: the setting wins, otherwise OLOOP_ACTOR, then the OS user name.</summary>
    public string ResolveActor()
    {
        if (!string.IsNullOrWhiteSpace(Actor))
            return Actor.Trim();

        var fromEnv = Environment.GetEnvironmentVariable("OLOOP_ACTOR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        var userName = Environment.UserName;
        return string.IsNullOrWhiteSpace(userName) ? "oloop" : userName;
    }

    /// <summary>Reads the settings file. A missing or broken file yields defaults.</summary>
    public static OloopSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
                return new OloopSettings();

            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<OloopSettings>(json) ?? new OloopSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new OloopSettings();
        }
    }

    /// <summary>Writes the settings file, creating the folder if needed.</summary>
    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        var folder = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(file, json);
    }
}
