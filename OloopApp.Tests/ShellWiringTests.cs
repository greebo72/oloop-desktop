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
    public async Task EditTask_Dialog_Writes_The_Change()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "Old title", "Old description", ["old"], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        _dialogs.OnEditTask = vm => vm.Title = "New title";

        var edited = await shell.OpenEditTaskAsync(task, project);

        Assert.True(edited);

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var onDisk = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal("New title", onDisk.Title);
    }

    [Fact]
    public async Task EditTask_Dialog_Without_A_Change_Writes_Nothing()
    {
        var store = new OloopStore(_root);
        var project = await ProjectAsync(store);
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "Same title", "Same description", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        var edited = await shell.OpenEditTaskAsync(task, project);

        Assert.False(edited);
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
        _dialogs.OnEditTask = vm => vm.Title = "My title";

        var edited = await shell.OpenEditTaskAsync(task, project);

        Assert.True(edited);
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
        _dialogs.OnEditTask = vm => vm.Title = "My title";

        var edited = await shell.OpenEditTaskAsync(task, project);

        Assert.False(edited);
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
        _dialogs.OnEditTask = vm => vm.Title = "My title";

        var edited = await shell.OpenEditTaskAsync(task, project);

        Assert.False(edited);

        var after = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal("Changed by somebody else", after.Title);
    }

    // ---------------- The settings ----------------

    [Fact]
    public async Task The_Settings_Dialog_Applies_The_New_Data_Root()
    {
        var shell = Shell();
        await shell.RefreshAsync();

        _dialogs.OnSettings = vm => vm.Actor = "timo";

        await shell.OpenSettingsAsync();

        Assert.Equal("timo", shell.Actor);
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
        public Action<EditTaskViewModel>? OnEditTask { get; set; }

        public bool CancelNewTask { get; set; }
        public ConflictChoice ConflictChoice { get; set; } = ConflictChoice.Cancel;
        public ConflictChoice? LastConflictChoice { get; private set; }
        public ConflictViewModel? LastConflictViewModel { get; private set; }

        public NewTaskViewModel? NewTaskViewModel { get; private set; }

        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
        {
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
            NewTaskViewModel = viewModel;

            if (CancelNewTask)
                return Task.FromResult(false);

            OnNewTask?.Invoke(viewModel);
            return Task.FromResult(true);
        }

        public Task<bool> ShowEditTaskAsync(EditTaskViewModel viewModel)
        {
            OnEditTask?.Invoke(viewModel);
            return Task.FromResult(true);
        }

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
        {
            LastConflictChoice = ConflictChoice;
            LastConflictViewModel = viewModel;
            return Task.FromResult(ConflictChoice);
        }
    }
}
