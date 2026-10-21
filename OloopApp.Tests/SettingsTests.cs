using System.Text.Json;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The settings file: the values survive a restart, and the remembered task
/// types are only names — an unknown name must not break the app.
/// </summary>
public class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "oloop-settings-" + Guid.NewGuid().ToString("N"));

    private string _path => Path.Combine(_folder, "settings.json");

    public SettingsTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // ---------------- The remembered types ----------------

    [Fact]
    public void A_New_Settings_File_Has_No_Remembered_Types()
    {
        var settings = new OloopSettings();

        Assert.Empty(settings.RecentTypes);
        Assert.Empty(settings.RecentTaskTypes);
    }

    [Fact]
    public void Closed_Tasks_Are_Hidden_By_Default()
    {
        // app-brief.md: "Closed tasks are hidden by default, with a switch
        // to show them." A new settings file must not show them either.
        var settings = new OloopSettings();

        Assert.False(settings.ShowClosed);
    }

    [Fact]
    public void A_Remembered_Type_Comes_First()
    {
        var settings = new OloopSettings();

        settings.RememberType(TaskType.bug);
        Assert.Equal([TaskType.bug], settings.RecentTaskTypes);

        settings.RememberType(TaskType.decision);
        Assert.Equal([TaskType.decision, TaskType.bug], settings.RecentTaskTypes);
    }

    [Fact]
    public void A_Remembered_Type_Comes_Only_Once()
    {
        var settings = new OloopSettings();

        settings.RememberType(TaskType.bug);
        settings.RememberType(TaskType.decision);
        settings.RememberType(TaskType.bug);

        Assert.Equal([TaskType.bug, TaskType.decision], settings.RecentTaskTypes);
    }

    [Fact]
    public void Only_The_Last_Types_Are_Remembered()
    {
        var settings = new OloopSettings();

        foreach (var type in TaskTypeExtensions.All)
            settings.RememberType(type);

        Assert.Equal(OloopSettings.MaxRecentTypes, settings.RecentTypes.Count);
        Assert.Equal(
            [TaskType.bug, TaskType.feature, TaskType.decision],
            settings.RecentTaskTypes);
    }

    [Fact]
    public void An_Unknown_Type_Name_Is_Dropped()
    {
        var settings = new OloopSettings
        {
            RecentTypes = ["nonsense", "bug", "alsoNonsense"],
        };

        Assert.Equal([TaskType.bug], settings.RecentTaskTypes);
    }

    [Fact]
    public void The_Remembered_Types_Survive_A_Restart()
    {
        var settings = new OloopSettings { DataRoot = "/tmp/root", Actor = "timo" };
        settings.RememberType(TaskType.feature);
        settings.RememberType(TaskType.bug);
        settings.Save(_path);

        var loaded = OloopSettings.Load(_path);

        Assert.Equal([TaskType.bug, TaskType.feature], loaded.RecentTaskTypes);
        Assert.Equal("/tmp/root", loaded.DataRoot);
        Assert.Equal("timo", loaded.Actor);
    }

    [Fact]
    public void A_Settings_File_Without_Recent_Types_Still_Loads()
    {
        File.WriteAllText(_path, """{ "dataRoot": "/tmp/root", "actor": "timo" }""");

        var loaded = OloopSettings.Load(_path);

        Assert.Empty(loaded.RecentTypes);
        Assert.Equal("/tmp/root", loaded.DataRoot);
    }

    [Fact]
    public void A_Settings_File_With_A_Null_Type_List_Still_Loads()
    {
        File.WriteAllText(_path, """{ "recentTypes": null }""");

        var loaded = OloopSettings.Load(_path);

        Assert.Empty(loaded.RecentTypes);
    }

    [Fact]
    public void The_Remembered_Types_Are_Written_As_Names()
    {
        var settings = new OloopSettings();
        settings.RememberType(TaskType.question);
        settings.Save(_path);

        using var document = JsonDocument.Parse(File.ReadAllText(_path));

        Assert.Equal("question", document.RootElement.GetProperty("recentTypes")[0].GetString());
    }

    [Fact]
    public void A_Settings_File_With_Other_Capitalization_Still_Loads()
    {
        // A file written by an older version or by hand may spell the names
        // differently — it still has to load.
        File.WriteAllText(_path, """{ "DataRoot": "/tmp/root", "Actor": "timo" }""");

        var loaded = OloopSettings.Load(_path);

        Assert.Equal("/tmp/root", loaded.DataRoot);
        Assert.Equal("timo", loaded.Actor);
    }

    [Fact]
    public void The_Settings_File_Holds_Only_The_Known_Keys()
    {
        // The computed values — the remembered types as enum, the path of the
        // file — must not end up in the file.
        var settings = new OloopSettings { FilePath = _path };
        settings.RememberType(TaskType.bug);
        settings.Save();

        using var document = JsonDocument.Parse(File.ReadAllText(_path));

        var keys = document.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["actor", "dataRoot", "grouping", "recentTypes", "showClosed", "staleDays"],
            keys);
    }

    // ---------------- The actor ----------------

    [Fact]
    public void The_Actor_In_The_Settings_Wins()
    {
        var settings = new OloopSettings { Actor = "  timo  " };

        Assert.Equal("timo", settings.ResolveActor());
    }

    [Fact]
    public void The_Actor_Falls_Back_To_The_Environment()
    {
        var settings = new OloopSettings { EnvironmentActorReader = () => "claude" };

        Assert.Equal("claude", settings.ResolveActor());
    }

    [Fact]
    public void The_Actor_Falls_Back_To_The_User_Of_The_System()
    {
        var settings = new OloopSettings
        {
            SystemUserName = "timo",
            EnvironmentActorReader = () => null,
        };

        // Without a name in the settings and without one in the environment,
        // the name of the user who is logged in is used.
        Assert.Equal("timo", settings.ResolveActor());
    }

    [Fact]
    public void The_Actor_Is_Empty_When_The_Chain_Runs_Out()
    {
        // No name in the settings, none in the environment and no user name
        // of the system: then there is no actor, and the app asks for one.
        // A made up name would end up in every task.
        var settings = new OloopSettings
        {
            SystemUserName = "",
            EnvironmentActorReader = () => null,
        };

        Assert.Equal("", settings.ResolveActor());
    }

    [Fact]
    public void The_Environment_Wins_Over_The_User_Of_The_System()
    {
        var settings = new OloopSettings
        {
            SystemUserName = "timo",
            EnvironmentActorReader = () => "claude",
        };

        Assert.Equal("claude", settings.ResolveActor());
    }
}
