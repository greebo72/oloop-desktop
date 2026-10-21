using System.Text.Json;
using System.Text.Json.Serialization;
using OloopApp.Core.Models;

namespace OloopApp.Core.Io;

/// <summary>
/// The app settings. Stored as JSON next to the app data, so the data root
/// and the actor survive a restart.
/// </summary>
public sealed class OloopSettings
{
    /// <summary>How many task types are remembered.</summary>
    public const int MaxRecentTypes = 3;

    /// <summary>
    /// The file the settings are read from and written to. The default is the
    /// settings file of the user; a test or the app can point it somewhere
    /// else. It is not part of the file itself.
    /// </summary>
    [JsonIgnore]
    public string FilePath { get; set; } = DefaultPath;

    /// <summary>The data root. Falls back to the OLOOP_ROOT environment variable.</summary>
    public string? DataRoot { get; set; }

    /// <summary>The actor written into createdBy/updatedBy. Falls back to OLOOP_ACTOR, then the OS user.</summary>
    public string Actor { get; set; } = "";

    /// <summary>
    /// The name of the system user — the last fallback of the actor.
    ///
    /// It is a property and not a direct call of <see cref="Environment.UserName"/>,
    /// so the fallback can be emptied in a test: that is the only way the
    /// chain of the actor can run out and the app has to ask for a name.
    ///
    /// It is not part of the settings file.
    /// </summary>
    [JsonIgnore]
    public string SystemUserName { get; set; } = Environment.UserName;

    /// <summary>
    /// Reads OLOOP_ACTOR — the second fallback of the actor.
    ///
    /// It goes through a function so a test can hand in its own reader
    /// instead of changing the environment of the whole process.
    ///
    /// It is not part of the settings file.
    /// </summary>
    [JsonIgnore]
    public Func<string?> EnvironmentActorReader { get; set; } =
        () => Environment.GetEnvironmentVariable("OLOOP_ACTOR");

    /// <summary>Whether closed tasks are shown in the list. Off by default.</summary>
    public bool ShowClosed { get; set; }

    /// <summary>The grouping mode name (None, Created, Modified, Type, Status).</summary>
    public string Grouping { get; set; } = "None";

    /// <summary>The "silent since" filter in days. Null disables it.</summary>
    public double? StaleDays { get; set; }

    private List<string>? _recentTypes;

    /// <summary>
    /// The task types the user picked last, most recent first. The new task
    /// dialog starts with the most recent one.
    ///
    /// The names are stored as text, so a settings file with an unknown name
    /// does not break the app.
    /// </summary>
    public List<string> RecentTypes
    {
        get => _recentTypes ??= [];
        set => _recentTypes = value ?? [];
    }

    /// <summary>
    /// The remembered types, in the order they were used. A name that is not a
    /// task type is dropped — an old or hand edited settings file must not
    /// break the dialog.
    ///
    /// It is computed from <see cref="RecentTypes"/>, so it is not written to
    /// the settings file.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<TaskType> RecentTaskTypes =>
        RecentTypes
            .Select(name => Enum.TryParse<TaskType>(name, ignoreCase: true, out var type)
                ? type
                : (TaskType?)null)
            .Where(type => type is not null)
            .Select(type => type!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Puts a type at the front of the remembered types. The list keeps at
    /// most <see cref="MaxRecentTypes"/> entries.
    /// </summary>
    public void RememberType(TaskType type)
    {
        var name = type.ToString();

        RecentTypes.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        RecentTypes.Insert(0, name);

        while (RecentTypes.Count > MaxRecentTypes)
            RecentTypes.RemoveAt(RecentTypes.Count - 1);
    }

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

    /// <summary>
    /// The actor: the setting wins, otherwise OLOOP_ACTOR, then the OS user
    /// name.
    ///
    /// The chain can end empty — on a system without a user name. Then the
    /// app asks for the name before the first write, because a made up name
    /// would end up in every task.
    /// </summary>
    public string ResolveActor()
    {
        if (!string.IsNullOrWhiteSpace(Actor))
            return Actor.Trim();

        var fromEnv = EnvironmentActorReader();
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        return string.IsNullOrWhiteSpace(SystemUserName) ? "" : SystemUserName.Trim();
    }

    /// <summary>Reads the settings file. A missing or broken file yields defaults.</summary>
    public static OloopSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
                return new OloopSettings { FilePath = file };

            var json = File.ReadAllText(file);
            var settings = JsonSerializer.Deserialize<OloopSettings>(json, ReadOptions)
                           ?? new OloopSettings();

            settings.FilePath = file;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new OloopSettings { FilePath = file };
        }
    }

    /// <summary>Writes the settings file, creating the folder if needed.</summary>
    public void Save(string? path = null)
    {
        var file = path ?? FilePath;
        var folder = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        var json = JsonSerializer.Serialize(this, WriteOptions);
        File.WriteAllText(file, json);
    }

    /// <summary>
    /// The options the settings file is written with: the keys are camelCase
    /// (dataRoot, actor, showClosed, grouping, staleDays, recentTypes), the
    /// same names the spec uses.
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The options the settings file is read with. The names are matched
    /// without regard to case, so a file written by hand or by an older
    /// version still loads.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
