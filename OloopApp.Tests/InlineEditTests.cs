using Avalonia.Headless;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The detail pane edits a task in place: title, description and labels turn
/// into input fields with an explicit Save, and input that is not written yet
/// is not lost silently.
/// </summary>
public class InlineEditTests : IDisposable
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(InlineEditTests).Assembly);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-inline-" + Guid.NewGuid().ToString("N"));

    private readonly FakeDialogs _dialogs = new();

    public InlineEditTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MainViewModel Shell()
    {
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

    /// <summary>A project with two tasks, opened in the split view.</summary>
    private async Task<(MainViewModel Shell, OloopStore Store, OloopProject Project, OloopTask First, OloopTask Second)> OpenAsync()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("My Project");
        var first = await store.CreateTaskAsync(project, TaskType.todo, "First", "First body", ["a"], "claude");
        var second = await store.CreateTaskAsync(project, TaskType.bug, "Second", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();
        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());

        return (shell, store, project, first, second);
    }

    private static TaskRowViewModel Row(MainViewModel shell, string title)
        => shell.Split!.TaskList.Groups.SelectMany(g => g.Rows).Single(r => r.Task.Title == title);

    private async Task<OloopTask> ReadAsync(OloopProject project, string id)
    {
        var path = Path.Combine(_root, "my-project", "tasks", id + ".md");
        return TaskIo.Read(id, await File.ReadAllTextAsync(path), project.Id);
    }

    // ---------------- The edit mode ----------------

    [Fact]
    public async Task Edit_Starts_With_The_Values_Of_The_Task_And_Counts_As_Unchanged()
    {
        var (shell, _, _, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;

        Assert.False(detail.IsEditing);

        detail.BeginEdit();

        Assert.True(detail.IsEditing);
        Assert.Equal("First", detail.Editor!.Title);
        Assert.Equal("First body", detail.Editor.Description);
        Assert.Equal("a", detail.Editor.LabelsText);
        Assert.False(detail.HasUnsavedChanges);
        Assert.False(shell.HasUnsavedChanges);
        Assert.Equal(first.Id, detail.Task.Id);
    }

    [Fact]
    public async Task Typing_Registers_Unsaved_Changes_And_Cancel_Drops_Them()
    {
        var (shell, _, _, _, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();

        detail.Editor!.Title = "First, changed";

        Assert.True(detail.HasUnsavedChanges);
        Assert.True(shell.HasUnsavedChanges);
        Assert.Equal(["First"], shell.UnsavedTaskTitles);

        detail.CancelCommand.Execute(null);

        Assert.False(detail.IsEditing);
        Assert.False(shell.HasUnsavedChanges);
    }

    [Fact]
    public async Task An_Empty_Title_Cannot_Be_Saved()
    {
        var (shell, _, _, _, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();

        detail.Editor!.Title = "  ";

        Assert.False(detail.CanSave);

        await detail.SaveCommand.ExecuteAsync(null);

        // Nothing happened: still editing, nothing written.
        Assert.True(detail.IsEditing);
    }

    [Fact]
    public async Task Close_Reopen_And_Comment_Wait_While_Editing()
    {
        var (shell, _, _, _, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.CommentText = "A comment";

        Assert.True(detail.CloseCommand.CanExecute(null));
        Assert.True(detail.CanAddComment);

        detail.BeginEdit();

        Assert.False(detail.CloseCommand.CanExecute(null));
        Assert.False(detail.ReopenCommand.CanExecute(null));
        Assert.False(detail.CanAddComment);

        detail.CancelEdit();

        Assert.True(detail.CloseCommand.CanExecute(null));
        Assert.True(detail.CanAddComment);
    }

    // ---------------------------------- Save ----------------------------------

    [Fact]
    public async Task Save_Writes_The_Task_And_Leaves_The_Edit_Mode()
    {
        var (shell, _, project, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "First, saved";
        detail.Editor.Description = "New body";
        detail.Editor.LabelsText = "x, y";

        await detail.SaveCommand.ExecuteAsync(null);

        var onDisk = await ReadAsync(project, first.Id);
        Assert.Equal("First, saved", onDisk.Title);
        Assert.Equal("New body", onDisk.Description?.Trim());
        Assert.Equal(["x", "y"], onDisk.Labels);
        Assert.Equal("claude", onDisk.UpdatedBy);

        // The list built the detail anew from the file: it shows the saved
        // task and is not editing any more.
        Assert.NotSame(detail, list.Selected);
        Assert.Equal("First, saved", list.Selected!.Title);
        Assert.False(list.Selected.IsEditing);
        Assert.False(detail.IsEditing);
        Assert.False(shell.HasUnsavedChanges);
    }

    [Fact]
    public async Task Save_Without_A_Change_Writes_Nothing()
    {
        var (shell, _, project, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();

        await detail.SaveCommand.ExecuteAsync(null);

        Assert.False(detail.IsEditing);
        Assert.Equal(first.UpdatedAt, (await ReadAsync(project, first.Id)).UpdatedAt);
    }

    [Fact]
    public async Task Save_Keeps_A_Typed_Comment()
    {
        var (shell, _, _, _, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.CommentText = "Not sent yet";
        detail.BeginEdit();
        detail.Editor!.Title = "First, saved";

        await detail.SaveCommand.ExecuteAsync(null);

        // The comment moved over to the detail of the saved task.
        Assert.NotSame(detail, list.Selected);
        Assert.Equal("Not sent yet", list.Selected!.CommentText);
        Assert.True(shell.HasUnsavedChanges);
        Assert.Same(list.Selected, shell.TryGetDraft(list.Selected.Task.Id));
    }

    // ---------------------------------- Conflicts ----------------------------------

    private async Task ChangeBehindTheBackAsync(OloopStore store, OloopProject project, OloopTask task)
    {
        var onDisk = await ReadAsync(project, task.Id);
        await store.WriteTaskAsync(
            project,
            onDisk.With(title: "Changed by somebody else", updatedAt: "2026-10-20T10:00:00Z", updatedBy: "other"),
            expectedUpdatedAt: onDisk.UpdatedAt);
    }

    [Fact]
    public async Task A_Cancelled_Conflict_Keeps_What_The_User_Typed()
    {
        var (shell, store, project, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "My title";

        await ChangeBehindTheBackAsync(store, project, first);
        _dialogs.ConflictChoice = ConflictChoice.Cancel;

        await detail.SaveCommand.ExecuteAsync(null);

        Assert.True(detail.IsEditing);
        Assert.Equal("My title", detail.Editor.Title);
        Assert.Same(detail, shell.TryGetDraft(first.Id));
        Assert.Equal("Changed by somebody else", (await ReadAsync(project, first.Id)).Title);
    }

    [Fact]
    public async Task A_Reload_Drops_The_Edit_On_Purpose()
    {
        var (shell, store, project, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "My title";

        await ChangeBehindTheBackAsync(store, project, first);
        _dialogs.ConflictChoice = ConflictChoice.Reload;

        await detail.SaveCommand.ExecuteAsync(null);

        Assert.False(detail.IsEditing);
        Assert.False(shell.HasUnsavedChanges);
        Assert.Equal("Changed by somebody else", list.Selected!.Title);
        Assert.False(list.Selected.IsEditing);
    }

    [Fact]
    public async Task An_Overwrite_Writes_The_Edit_Over_The_Version_On_Disk()
    {
        var (shell, store, project, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "My title";

        await ChangeBehindTheBackAsync(store, project, first);
        _dialogs.ConflictChoice = ConflictChoice.Overwrite;

        await detail.SaveCommand.ExecuteAsync(null);

        Assert.Equal("My title", (await ReadAsync(project, first.Id)).Title);
        Assert.False(shell.HasUnsavedChanges);
    }

    // ---------------------------------- Navigation ----------------------------------

    [Fact]
    public async Task An_Edit_Survives_Selecting_Another_Task()
    {
        var (shell, _, _, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "First, half typed";

        list.SelectRow(Row(shell, "Second"));
        Assert.Equal("Second", list.Selected!.Title);
        Assert.False(list.Selected.IsEditing);
        Assert.True(shell.HasUnsavedChanges);

        list.SelectRow(Row(shell, "First"));

        Assert.Same(detail, list.Selected);
        Assert.True(list.Selected!.IsEditing);
        Assert.Equal("First, half typed", list.Selected.Editor!.Title);
        Assert.Equal(first.Id, list.Selected.Task.Id);
    }

    [Fact]
    public async Task An_Edit_Survives_A_Refresh()
    {
        var (shell, _, _, _, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "First, half typed";

        await shell.RefreshAsync();

        Assert.Same(detail, list.Selected);
        Assert.Equal("First, half typed", list.Selected!.Editor!.Title);

        // Typing in the search field refreshes the list, too.
        shell.SearchText = "First";

        Assert.Same(detail, list.Selected);
    }

    [Fact]
    public async Task An_Edit_Survives_Leaving_And_Reopening_The_Project()
    {
        var (shell, _, _, _, _) = await OpenAsync();

        shell.Split!.TaskList.SelectRow(Row(shell, "First"));
        var detail = shell.Split.TaskList.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "First, half typed";

        shell.BackToDashboardCommand.Execute(null);
        Assert.Null(shell.Split);
        Assert.True(shell.HasUnsavedChanges);

        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());
        shell.Split!.TaskList.SelectRow(Row(shell, "First"));

        Assert.Same(detail, shell.Split.TaskList.Selected);
        Assert.Equal("First, half typed", shell.Split.TaskList.Selected!.Editor!.Title);
    }

    [Fact]
    public async Task A_Typed_Comment_Survives_A_Refresh_And_Another_Selection()
    {
        var (shell, _, _, _, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        list.Selected!.CommentText = "Not sent yet";

        await shell.RefreshAsync();
        Assert.Equal("Not sent yet", list.Selected!.CommentText);

        list.SelectRow(Row(shell, "Second"));
        Assert.Equal("", list.Selected!.CommentText);

        list.SelectRow(Row(shell, "First"));
        Assert.Equal("Not sent yet", list.Selected!.CommentText);
        Assert.True(shell.HasUnsavedChanges);
    }

    [Fact]
    public async Task Adding_The_Comment_Empties_The_Field_And_Leaves_No_Draft()
    {
        var (shell, _, project, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        var detail = list.Selected!;
        detail.CommentText = "Sent now";

        await detail.AddCommentCommand.ExecuteAsync(null);

        Assert.Equal("Sent now", (await ReadAsync(project, first.Id)).Comments.Single().Body);
        Assert.Equal("", list.Selected!.CommentText);
        Assert.False(shell.HasUnsavedChanges);
        Assert.Single(list.Selected.Comments);
    }

    [Fact]
    public async Task A_Task_That_Is_Gone_Takes_Its_Draft_Along()
    {
        var (shell, _, project, first, _) = await OpenAsync();
        var list = shell.Split!.TaskList;

        list.SelectRow(Row(shell, "First"));
        list.Selected!.BeginEdit();
        list.Selected.Editor!.Title = "First, half typed";
        Assert.True(shell.HasUnsavedChanges);

        File.Delete(Path.Combine(_root, "my-project", "tasks", first.Id + ".md"));
        await shell.RefreshAsync();

        Assert.False(shell.HasUnsavedChanges);
        Assert.Contains("no longer", shell.StatusMessage);
        Assert.NotNull(project);
    }

    // ---------------------------------- Entry points ----------------------------------

    [Fact]
    public async Task The_Silent_View_Edits_In_Place_Too()
    {
        var (shell, _, _, _, _) = await OpenAsync();

        shell.OpenSilentCommand.Execute(null);
        var silent = shell.Silent!;
        silent.MinimumAgeDays = 0;

        var row = silent.Groups.SelectMany(g => g.Rows).First(r => r.Task.Title == "First");
        await silent.EditRowAsync(row);

        Assert.True(silent.Selected!.IsEditing);
        Assert.Equal("First", silent.Selected.Editor!.Title);
    }

    // ---------------------------------- The window ----------------------------------

    [Fact]
    public async Task The_Window_Asks_Before_It_Closes_With_Unsaved_Changes()
    {
        var (shell, _, _, _, _) = await OpenAsync();
        shell.Split!.TaskList.SelectRow(Row(shell, "First"));
        shell.Split.TaskList.Selected!.BeginEdit();
        shell.Split.TaskList.Selected.Editor!.Title = "First, half typed";

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow { DataContext = shell };
            window.Show();

            // "Keep editing": the window stays.
            _dialogs.DiscardAnswer = false;
            window.Close();
            await Task.Delay(50);

            Assert.True(window.IsVisible);
            Assert.Equal([["First"]], _dialogs.DiscardAsked);

            // "Discard": the window closes.
            _dialogs.DiscardAnswer = true;
            window.Close();
            await Task.Delay(50);

            Assert.False(window.IsVisible);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Window_Closes_Without_A_Question_When_Nothing_Is_Unsaved()
    {
        var (shell, _, _, _, _) = await OpenAsync();

        await Session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.Close();

            Assert.False(window.IsVisible);
            Assert.Empty(_dialogs.DiscardAsked);
        }, CancellationToken.None);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public ConflictChoice ConflictChoice { get; set; } = ConflictChoice.Cancel;
        public bool DiscardAnswer { get; set; }
        public List<string[]> DiscardAsked { get; } = [];

        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel) => Task.FromResult(false);

        public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
            => Task.FromResult<OloopProject?>(null);

        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel) => Task.FromResult(false);

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
            => Task.FromResult(ConflictChoice);

        public Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles)
        {
            DiscardAsked.Add([.. taskTitles]);
            return Task.FromResult(DiscardAnswer);
        }
    }
}
