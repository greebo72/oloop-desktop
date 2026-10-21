using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The task list has an edit command: every row carries a button that opens
/// the edit dialog of that task, and the shortcut edits the selected task.
/// </summary>
public class EditCommandTests : IDisposable
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditCommandTests).Assembly);

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "oloop-edit-" + Guid.NewGuid().ToString("N"));

    private readonly string _root;

    public EditCommandTests()
    {
        Directory.CreateDirectory(_folder);
        _root = Path.Combine(_folder, "root");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T typed)
                yield return typed;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static OloopProject Project() => new()
    {
        FormatVersion = 1,
        Id = "11111111-1111-4111-8111-111111111111",
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = "example-project",
    };

    private static OloopTask SampleTask(string title = "Write the docs") => new()
    {
        FormatVersion = 1,
        Id = "22222222-2222-4222-8222-222222222222",
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.todo,
        Title = title,
        Status = TaskStatus.open,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "alice",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "alice",
    };

    // ---------------- The command of the list ----------------

    [Fact]
    public void Every_Group_Has_An_Edit_Command()
    {
        var shell = new MainViewModel();
        shell.Tasks.Add(SampleTask());

        var list = new TaskListViewModel(
            Project(), shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

        Assert.Single(list.Groups);
        Assert.NotNull(list.Groups[0].EditCommand);
    }

    [Fact]
    public async Task The_Edit_Command_Of_A_Row_Edits_The_Task_In_Place()
    {
        var shell = new MainViewModel { DataRoot = _root, Actor = "claude" };
        var dialogs = new RecordingDialogService();
        shell.Dialogs = dialogs;

        var task = SampleTask();
        shell.Tasks.Add(task);

        var list = new TaskListViewModel(
            Project(), shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

        await list.Groups[0].EditCommand!.ExecuteAsync(list.Groups[0].Rows[0]);

        // The row is selected and its detail is in edit mode, there is no dialog.
        Assert.NotNull(list.Selected);
        Assert.True(list.Selected!.IsEditing);
        Assert.Equal("Write the docs", list.Selected.Editor!.TrimmedTitle);
    }

    [Fact]
    public async Task The_Edit_Command_Without_A_Row_Does_Nothing()
    {
        var shell = new MainViewModel { DataRoot = _root, Actor = "claude" };
        var dialogs = new RecordingDialogService();
        shell.Dialogs = dialogs;

        var list = new TaskListViewModel(
            Project(), shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

        await list.EditRowAsync(null);

        Assert.Null(list.Selected);
    }

    [Fact]
    public void The_Edit_Command_Edits_The_Selected_Task()
    {
        var shell = new MainViewModel { DataRoot = _root, Actor = "claude" };
        var dialogs = new RecordingDialogService();
        shell.Dialogs = dialogs;

        var task = SampleTask();
        shell.Tasks.Add(task);

        var list = new TaskListViewModel(
            Project(), shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

        // Nothing is selected — nothing happens.
        list.EditSelected();
        Assert.Null(list.Selected);

        list.SelectRow(list.Groups[0].Rows[0]);
        Assert.False(list.Selected!.IsEditing);

        list.EditSelected();

        Assert.True(list.Selected.IsEditing);
        Assert.Equal("Write the docs", list.Selected.Editor!.TrimmedTitle);
    }

    // ---------------- The button in the view ----------------

    [Fact]
    public async Task Every_Row_Has_An_Edit_Button()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = Project();
            var task = SampleTask();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [project],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

            window.UpdateLayout();

            var list = Descendants<TaskListView>(window).FirstOrDefault();
            Assert.NotNull(list);

            // The row carries the edit command — next to the button that
            // opens the detail.
            var edit = Descendants<Button>(list!)
                .FirstOrDefault(b => b.Command is IAsyncRelayCommand<TaskRowViewModel>);
            Assert.NotNull(edit);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Edit_Button_Of_A_Row_Reaches_The_Shell()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { DataRoot = _root, Actor = "claude" };
            var dialogs = new RecordingDialogService();
            shell.Dialogs = dialogs;

            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = Project();
            var task = SampleTask();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [project],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

            window.UpdateLayout();

            var list = Descendants<TaskListView>(window).FirstOrDefault();
            Assert.NotNull(list);

            var edit = Descendants<Button>(list!)
                .FirstOrDefault(b => b.Command is IAsyncRelayCommand<TaskRowViewModel>);
            Assert.NotNull(edit);

            edit!.Command!.Execute(edit.CommandParameter);

            var selected = shell.Split!.TaskList.Selected;
            Assert.NotNull(selected);
            Assert.True(selected!.IsEditing);
            Assert.Equal("Write the docs", selected.Editor!.TrimmedTitle);

            window.Close();
        });
    }

    /// <summary>A dialog service that answers every question with "no".</summary>
    private sealed class RecordingDialogService : IDialogService
    {
        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
            => Task.FromResult(false);

        public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
            => Task.FromResult<OloopProject?>(null);

        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel)
            => Task.FromResult(false);

        public Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles)
            => Task.FromResult(false);

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
            => Task.FromResult(ConflictChoice.Cancel);
    }
}
