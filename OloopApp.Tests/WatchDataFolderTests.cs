using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The app watches the data folder and reloads by itself when the CLI, another
/// client or the sync writes into it.
/// </summary>
public class WatchDataFolderTests : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-watch-" + Guid.NewGuid().ToString("N"));

    public WatchDataFolderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // The watcher keeps the folder open on some systems.
        GC.Collect();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, int milliseconds = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }

        return condition();
    }

    private MainViewModel Shell() => new(new OloopSettings
    {
        FilePath = Path.Combine(_root, "settings.json"),
        DataRoot = _root,
        Actor = "claude",
    })
    {
        DataRoot = _root,
        Actor = "claude",
        WatchDataFolder = true,
        WatchDebounce = Debounce,
    };

    // ---------------- The watcher ----------------

    [Fact]
    public async Task A_Burst_Of_Writes_Raises_One_Event_After_It_Ended()
    {
        using var watcher = new DataFolderWatcher(_root, Debounce);
        var raised = 0;
        watcher.Changed += () => Interlocked.Increment(ref raised);

        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"f{i}.md"), "x");
            await Task.Delay(10);
        }

        Assert.True(await WaitUntil(() => raised >= 1));
        await Task.Delay(500);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Events_In_The_Backup_Folder_And_Of_Temp_Files_Are_Ignored()
    {
        Directory.CreateDirectory(Path.Combine(_root, "p", ".backup"));
        using var watcher = new DataFolderWatcher(_root, Debounce);
        var raised = 0;
        watcher.Changed += () => Interlocked.Increment(ref raised);

        File.WriteAllText(Path.Combine(_root, "p", ".backup", "t.md.bak"), "x");
        File.WriteAllText(Path.Combine(_root, "p", ".t.md.abc.tmp"), "x");
        await Task.Delay(700);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void The_Fingerprint_Changes_With_Content_And_Not_With_Backups_Or_Temp_Files()
    {
        var store = new OloopStore(_root);
        var before = store.Fingerprint();
        Assert.Equal(before, store.Fingerprint());

        Directory.CreateDirectory(Path.Combine(_root, "p", ".backup"));
        File.WriteAllText(Path.Combine(_root, "p", ".backup", "t.md.bak"), "x");
        File.WriteAllText(Path.Combine(_root, "p", ".t.md.abc.tmp"), "x");
        Assert.Equal(before, store.Fingerprint());

        File.WriteAllText(Path.Combine(_root, "p", "t.md"), "x");
        var after = store.Fingerprint();
        Assert.NotEqual(before, after);

        File.WriteAllText(Path.Combine(_root, "p", "t.md"), "xy");
        Assert.NotEqual(after, store.Fingerprint());
    }

    // ---------------- The shell ----------------

    [Fact]
    public async Task A_Task_Written_From_Outside_Shows_Up_Without_A_Reload()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");
        await store.CreateTaskAsync(project, TaskType.todo, "First", "", [], "cli");

        using var shell = Shell();
        await shell.RefreshAsync();
        Assert.Single(shell.Tasks);

        // The CLI writes while the app runs.
        await store.CreateTaskAsync(project, TaskType.todo, "From the CLI", "", [], "cli");

        Assert.True(await WaitUntil(() => shell.Tasks.Count == 2));
        Assert.Contains(shell.Tasks, t => t.Title == "From the CLI");
    }

    [Fact]
    public async Task A_New_Project_And_A_Change_Of_A_Task_Show_Up()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "First", "", [], "cli");

        using var shell = Shell();
        await shell.RefreshAsync();

        await store.CreateProjectAsync("Beta");
        Assert.True(await WaitUntil(() => shell.Projects.Count == 2));

        await store.SetStatusAsync(task, project, true, "cli");
        Assert.True(await WaitUntil(() => shell.Tasks.Single(t => t.Id == task.Id).IsClosed));
    }

    [Fact]
    public async Task The_Apps_Own_Write_Reloads_Once_Not_Twice()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");

        using var shell = Shell();
        await shell.RefreshAsync();

        var reloads = 0;
        shell.DataReloaded += () => Interlocked.Increment(ref reloads);

        Assert.True(await shell.CreateTaskAsync(project, TaskType.todo, "Mine", "", []));
        Assert.Equal(1, reloads);

        // The file events of the write arrive after the debounce window. They
        // find what was loaded already and do not reload.
        await Task.Delay(1000);
        Assert.Equal(1, reloads);
        Assert.Single(shell.Tasks);
    }

    [Fact]
    public async Task Selection_And_A_Typed_Comment_Survive_A_Reload_From_Outside()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");
        await store.CreateTaskAsync(project, TaskType.todo, "Selected one", "", [], "cli");

        using var shell = Shell();
        await shell.RefreshAsync();
        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());

        var detail = shell.Split!.TaskList.Selected!;
        detail.CommentText = "half a thought";
        var id = detail.Task.Id;

        // Another task appears; the selected one does not change.
        await store.CreateTaskAsync(project, TaskType.todo, "Another", "", [], "cli");
        Assert.True(await WaitUntil(() => shell.Tasks.Count == 2));

        var after = shell.Split.TaskList.Selected;
        Assert.NotNull(after);
        Assert.Equal(id, after!.Task.Id);
        Assert.Same(detail, after);
        Assert.Equal("half a thought", after.CommentText);
        Assert.True(shell.HasUnsavedChanges);
    }

    [Fact]
    public async Task An_Edit_That_Is_Under_Way_Survives_A_Change_Of_The_Same_Task_From_Outside()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Original", "", [], "cli");

        using var shell = Shell();
        await shell.RefreshAsync();
        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());

        var detail = shell.Split!.TaskList.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "My edit";

        await store.AddCommentAsync(task, project, "from the CLI", "cli");
        Assert.True(await WaitUntil(() => shell.Tasks.Single().Comments.Count == 1));

        var after = shell.Split.TaskList.Selected!;
        Assert.True(after.IsEditing);
        Assert.Equal("My edit", after.Editor!.Title);
    }

    [Fact]
    public async Task Without_The_Switch_The_Shell_Does_Not_Watch()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");

        using var shell = Shell();
        shell.WatchDataFolder = false;
        await shell.RefreshAsync();

        await store.CreateTaskAsync(project, TaskType.todo, "Unseen", "", [], "cli");
        await Task.Delay(800);

        Assert.Empty(shell.Tasks);
    }
}
