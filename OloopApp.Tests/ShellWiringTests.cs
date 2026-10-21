using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The shell owns the write path: it collects the input of a dialog, writes it
/// with the store and asks the user what to do when the store refuses the
/// write because the file changed on disk.
///
/// The tests use a fake dialog service, so no window is ever opened.
/// </summary>
public class ShellWiringTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-shell-" + Guid.NewGuid().ToString("N"));

    private readonly FakeDialogService _dialogs;

    public ShellWiringTests()
    {
        Directory.CreateDirectory(_root);
        _dialogs = new FakeDialogService(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MainViewModel Shell()
    {
        // The shell writes the settings file when it remembers the type of
        // the last task — the test must not touch the settings of the user.
        var shell = new MainViewModel(new OloopSettings
        {
            FilePath = Path.Combine(_root, "settings.json"),
            DataRoot = _root,
            Actor = "claude",
        })
        {
            DataRoot = _root,
            Actor = "claude",
        };

        shell.Dialogs = _dialogs;
        return shell;
    }

    /// <summary>
    /// A shell without an actor: the settings file has no name, the
    /// environment has none and the system has no user name either. Then
    /// the app has to ask for the name before it writes.
    /// </summary>
    private MainViewModel ShellWithoutActor()
    {
        var settings = new OloopSettings
        {
            FilePath = Path.Combine(_root, "settings.json"),
            DataRoot = _root,
            SystemUserName = "",
            EnvironmentActorReader = () => null,
        };

        var shell = new MainViewModel(settings)
        {
            DataRoot = _root,
        };

        shell.Dialogs = _dialogs;
        return shell;
    }

    private async Task<OloopProject> ProjectAsync(OloopStore store)
        => await store.CreateProjectAsync("My Project");

    // ---------------- The dialogs are reachable ----------------

    [Fact]
    public async Task NewTask_Dialog_Creates_The_Task()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = Shell();

        _dialogs.OnNewTask = vm => vm.Title = "A new task";

        var created = await shell.OpenNewTaskAsync(project);

        Assert.True(created);
        Assert.Equal("A new task", _dialogs.NewTaskViewModel!.TrimmedTitle);

        // The task is on disk and the shell has reloaded it.
        Assert.Contains(shell.Tasks, t => t.Title == "A new task");
    }

    [Fact]
    public async Task NewTask_Dialog_Cancelled_Writes_Nothing()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = Shell();

        _dialogs.OnNewTask = _ => { };
        _dialogs.CancelNewTask = true;

        var created = await shell.OpenNewTaskAsync(project);

        Assert.False(created);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "my-project", "tasks")));
    }

    [Fact]
    public async Task SaveTaskEdit_Writes_The_Change()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "Old title", "Old description", ["old"], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        var outcome = await shell.SaveTaskEditAsync(
            task, project, "New title", task.Description ?? "", task.Labels);

        Assert.Equal(WriteOutcome.Saved, outcome);

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal("New title", onDisk.Title);
    }

    // ---------------- The status ----------------

    [Fact]
    public async Task Close_Task_Writes_The_Status()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "A task", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        var closed = await shell.SetTaskClosedAsync(task, project, closed: true);

        Assert.True(closed);

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal(TaskStatus.closed, onDisk.Status);
    }

    [Fact]
    public async Task Add_Comment_Appends_It()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "A task", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        var added = await shell.AddCommentAsync(task, project, "A comment");

        Assert.True(added);

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Single(onDisk.Comments);
        Assert.Equal("A comment", onDisk.Comments[0].Body);
    }

    // ---------------- The conflict ----------------

    [Fact]
    public async Task A_Conflict_Asks_The_User_And_Overwrites_On_Request()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "A task", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        // Somebody else writes the file behind the back of the app.
        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        await store.WriteTaskAsync(
            project,
            onDisk.With(title: "Changed by somebody else", updatedAt: "2026-10-20T10:00:00Z", updatedBy: "other"),
            expectedUpdatedAt: onDisk.UpdatedAt);

        _dialogs.ConflictChoice = ConflictChoice.Overwrite;

        var outcome = await shell.SaveTaskEditAsync(
            task, project, "My title", task.Description ?? "", task.Labels);

        Assert.Equal(WriteOutcome.Saved, outcome);
        Assert.Equal(ConflictChoice.Overwrite, _dialogs.LastConflictChoice);
        Assert.NotNull(_dialogs.LastConflictViewModel);

        var after = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal("My title", after.Title);
    }

    [Fact]
    public async Task A_Conflict_Reloads_On_Request()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "A task", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        await store.WriteTaskAsync(
            project,
            onDisk.With(title: "Changed by somebody else", updatedAt: "2026-10-20T10:00:00Z", updatedBy: "other"),
            expectedUpdatedAt: onDisk.UpdatedAt);

        _dialogs.ConflictChoice = ConflictChoice.Reload;

        var outcome = await shell.SaveTaskEditAsync(
            task, project, "My title", task.Description ?? "", task.Labels);

        Assert.Equal(WriteOutcome.Discarded, outcome);
        Assert.Equal(ConflictChoice.Reload, _dialogs.LastConflictChoice);

        // The version on disk is untouched and the shell shows it.
        var after = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal("Changed by somebody else", after.Title);
        Assert.Contains(shell.Tasks, t => t.Title == "Changed by somebody else");
    }

    [Fact]
    public async Task A_Conflict_Cancelled_Writes_Nothing()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "A task", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        await store.WriteTaskAsync(
            project,
            onDisk.With(title: "Changed by somebody else", updatedAt: "2026-10-20T10:00:00Z", updatedBy: "other"),
            expectedUpdatedAt: onDisk.UpdatedAt);

        _dialogs.ConflictChoice = ConflictChoice.Cancel;

        var outcome = await shell.SaveTaskEditAsync(
            task, project, "My title", task.Description ?? "", task.Labels);

        Assert.Equal(WriteOutcome.NotSaved, outcome);

        var after = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal("Changed by somebody else", after.Title);
    }

    // ---------------- The settings ----------------

    [Fact]
    public async Task The_Settings_Dialog_Applies_The_New_Data_Root()
    {
        var shell = Shell();
        await shell.RefreshAsync();

        _dialogs.OnSettings = vm => vm.Actor = "alice";

        await shell.OpenSettingsAsync();

        Assert.Equal("alice", shell.Actor);
    }

    // ---------------- The actor is asked for ----------------

    [Fact]
    public async Task A_Write_Without_Actor_Asks_For_The_Name_And_Writes_Afterwards()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = ShellWithoutActor();
        await shell.RefreshAsync();

        _dialogs.OnSettings = vm => vm.Actor = "alice";

        var created = await shell.CreateTaskAsync(
            project, TaskType.todo, "A task", "", []);

        Assert.True(created);
        Assert.Equal(1, _dialogs.SettingsShown);

        // The task is on disk and carries the name that was asked for.
        var files = Directory.EnumerateFiles(Path.Combine(_root, "my-project", "tasks")).ToList();
        Assert.Single(files);

        var onDisk = TaskIo.Read(
            Path.GetFileNameWithoutExtension(files[0]),
            await File.ReadAllTextAsync(files[0]),
            project.Id);

        Assert.Equal("alice", onDisk.CreatedBy);
        Assert.Equal("alice", onDisk.UpdatedBy);
    }

    [Fact]
    public async Task A_Write_Without_Actor_Writes_Nothing_When_The_Dialog_Is_Cancelled()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = ShellWithoutActor();
        await shell.RefreshAsync();

        _dialogs.CancelSettings = true;

        var created = await shell.CreateTaskAsync(
            project, TaskType.todo, "A task", "", []);

        Assert.False(created);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "my-project", "tasks")));
    }

    [Fact]
    public async Task A_Write_Without_Actor_Writes_Nothing_When_No_Name_Is_Entered()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = ShellWithoutActor();
        await shell.RefreshAsync();

        // The dialog opens but the name stays empty.
        _dialogs.OnSettings = vm => vm.Actor = "";

        var created = await shell.CreateTaskAsync(
            project, TaskType.todo, "A task", "", []);

        Assert.False(created);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "my-project", "tasks")));
    }

    [Fact]
    public async Task A_Write_With_Actor_Does_Not_Open_The_Settings()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = Shell();
        await shell.RefreshAsync();

        var created = await shell.CreateTaskAsync(
            project, TaskType.todo, "A task", "", []);

        Assert.True(created);
        Assert.Equal(0, _dialogs.SettingsShown);
    }

    [Fact]
    public async Task Closing_A_Task_Without_Actor_Asks_For_The_Name()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "A task", "", [], "claude");

        var shell = ShellWithoutActor();
        await shell.RefreshAsync();

        _dialogs.OnSettings = vm => vm.Actor = "alice";

        var closed = await shell.SetTaskClosedAsync(task, project, closed: true);

        Assert.True(closed);
        Assert.Equal(1, _dialogs.SettingsShown);

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal(TaskStatus.closed, onDisk.Status);
        Assert.Equal("alice", onDisk.UpdatedBy);
    }

    [Fact]
    public async Task The_New_Task_Dialog_Asks_For_The_Name_Before_It_Opens()
    {
        // The user should not type a whole task first and only then be asked
        // who they are.
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = ShellWithoutActor();
        await shell.RefreshAsync();

        _dialogs.OnSettings = vm => vm.Actor = "alice";
        _dialogs.OnNewTask = vm => vm.Title = "A task";

        var created = await shell.OpenNewTaskAsync(project);

        Assert.True(created);
        Assert.Equal(1, _dialogs.SettingsShown);
        Assert.Equal(1, _dialogs.NewTaskShown);
    }

    [Fact]
    public async Task The_New_Task_Dialog_Stays_Closed_Without_A_Name()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var shell = ShellWithoutActor();
        await shell.RefreshAsync();

        _dialogs.CancelSettings = true;

        var created = await shell.OpenNewTaskAsync(project);

        Assert.False(created);
        Assert.Equal(0, _dialogs.NewTaskShown);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "my-project", "tasks")));
    }

    // ---------------- The fake ----------------

    /// <summary>
    /// A dialog service that never opens a window: it fills the view models
    /// and returns what the test wants.
    /// </summary>
    private sealed class FakeDialogService : IDialogService
    {
        private readonly string _root;

        public FakeDialogService(string root)
        {
            _root = root;
        }

        public Action<SettingsViewModel>? OnSettings { get; set; }
        public Action<NewTaskViewModel>? OnNewTask { get; set; }

        public bool CancelNewTask { get; set; }
        public bool CancelSettings { get; set; }
        public ConflictChoice ConflictChoice { get; set; } = ConflictChoice.Cancel;
        public ConflictChoice? LastConflictChoice { get; private set; }
        public ConflictViewModel? LastConflictViewModel { get; private set; }

        public NewTaskViewModel? NewTaskViewModel { get; private set; }

        /// <summary>How often the settings were shown.</summary>
        public int SettingsShown { get; private set; }

        /// <summary>How often the new task dialog was shown.</summary>
        public int NewTaskShown { get; private set; }

        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
        {
            SettingsShown++;

            if (CancelSettings)
                return Task.FromResult(false);

            if (OnSettings is null)
                return Task.FromResult(false);

            OnSettings(viewModel);
            viewModel.SaveTo(Path.Combine(_root, "settings.json"));
            return Task.FromResult(true);
        }

        public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
            => Task.FromResult<OloopProject?>(null);

        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel)
        {
            NewTaskShown++;
            NewTaskViewModel = viewModel;

            if (CancelNewTask)
                return Task.FromResult(false);

            OnNewTask?.Invoke(viewModel);
            return Task.FromResult(true);
        }

        public Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles)
            => Task.FromResult(false);

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
        {
            LastConflictChoice = ConflictChoice;
            LastConflictViewModel = viewModel;
            return Task.FromResult(ConflictChoice);
        }
    }
}
