using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using OloopApp.Controls;
using OloopApp.Core.Filters;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The label input of the task dialogs: suggestions while typing, and the
/// spelling of a new label that only differs in case from an existing one.
/// </summary>
public class LabelInputTests : IDisposable
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(LabelInputTests).Assembly);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-labels-" + Guid.NewGuid().ToString("N"));

    private readonly NewTaskDialogs _dialogs = new();

    public LabelInputTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    private static readonly LabelEntry[] Known =
    [
        new("ui", 20),
        new("parked", 12),
        new("Bug", 7),
        new("spec", 9),
        new("performance", 4),
    ];

    private static OloopProject Project() => new()
    {
        FormatVersion = 1,
        Id = "11111111-1111-4111-8111-111111111111",
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = "example-project",
    };

    private static OloopTask MakeTask(params string[] labels) => new()
    {
        FormatVersion = 1,
        Id = Guid.NewGuid().ToString("D"),
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.todo,
        Title = "Write the docs",
        Status = Core.Models.TaskStatus.open,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "alice",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "alice",
        Labels = labels,
    };

    // ---------------- The view models ----------------

    [Fact]
    public void The_New_Task_Dialog_Writes_A_New_Label_In_The_Existing_Spelling()
    {
        var vm = new NewTaskViewModel(Project(), null, Known) { LabelsText = "bug, ui, brand-new" };

        Assert.Equal(["Bug", "ui", "brand-new"], vm.Labels);
        var change = Assert.Single(vm.AdoptedLabels);
        Assert.Equal(("bug", "Bug"), (change.Typed, change.Adopted));
    }

    [Fact]
    public void Without_Known_Labels_Nothing_Changes()
    {
        var vm = new NewTaskViewModel(Project()) { LabelsText = "bug, ui" };

        Assert.Equal(["bug", "ui"], vm.Labels);
        Assert.Empty(vm.AdoptedLabels);
        Assert.Empty(vm.KnownLabels);
    }

    [Fact]
    public void An_Old_Label_Of_The_Task_Keeps_Its_Spelling_In_The_Editor()
    {
        // The task carries "bug", the rest of the data says "Bug".
        var vm = new EditTaskViewModel(MakeTask("bug"), Known);

        Assert.Equal(["bug"], vm.Labels);
        Assert.Empty(vm.AdoptedLabels);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void A_Label_Added_In_The_Editor_Takes_The_Existing_Spelling_And_Counts_As_A_Change()
    {
        var vm = new EditTaskViewModel(MakeTask("ui"), Known);

        vm.LabelsText = "ui, bug";

        Assert.Equal(["ui", "Bug"], vm.Labels);
        Assert.True(vm.IsDirty);
        Assert.Single(vm.AdoptedLabels);
    }

    // ---------------- Saving ----------------

    private async Task<(MainViewModel Shell, OloopStore Store, OloopProject Project)> OpenAsync(
        params (string Title, string[] Labels)[] tasks)
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("My Project");
        foreach (var (title, labels) in tasks)
            await store.CreateTaskAsync(project, TaskType.todo, title, "", labels, "claude");

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

        await shell.RefreshAsync();
        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());
        return (shell, store, project);
    }

    private async Task<OloopTask> ReadAsync(OloopProject project, string id)
        => TaskIo.Read(id, await File.ReadAllTextAsync(
            Path.Combine(_root, "my-project", "tasks", id + ".md")), project.Id);

    [Fact]
    public async Task Saving_An_Edit_Writes_The_Existing_Spelling_And_Says_So()
    {
        var (shell, _, project) = await OpenAsync(
            ("One", ["Bug"]), ("Two", ["Bug"]), ("Three", ["ui"]));
        var list = shell.Split!.TaskList;

        list.SelectRow(list.Groups.SelectMany(g => g.Rows).Single(r => r.Task.Title == "Three"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.LabelsText = "ui, bug";

        await detail.SaveCommand.ExecuteAsync(null);

        var onDisk = await ReadAsync(project, detail.Task.Id);
        Assert.Equal(["ui", "Bug"], onDisk.Labels);
        Assert.Contains("Label 'bug' as 'Bug' saved.", shell.StatusMessage);
        Assert.Contains("Task 'Three' saved.", shell.StatusMessage);
    }

    [Fact]
    public async Task Saving_Another_Change_Leaves_An_Old_Label_In_Its_Spelling()
    {
        // Two tasks say "Bug", the edited one says "bug" — and keeps it.
        var (shell, _, project) = await OpenAsync(
            ("One", ["Bug"]), ("Two", ["Bug"]), ("Three", ["bug"]));
        var list = shell.Split!.TaskList;

        list.SelectRow(list.Groups.SelectMany(g => g.Rows).Single(r => r.Task.Title == "Three"));
        var detail = list.Selected!;
        detail.BeginEdit();
        detail.Editor!.Title = "Three, renamed";

        await detail.SaveCommand.ExecuteAsync(null);

        var onDisk = await ReadAsync(project, detail.Task.Id);
        Assert.Equal(["bug"], onDisk.Labels);
        Assert.DoesNotContain("Label", shell.StatusMessage ?? "");
    }

    [Fact]
    public async Task The_New_Task_Dialog_Gets_The_Labels_Of_The_Project_And_The_Status_Says_What_Changed()
    {
        var (shell, store, project) = await OpenAsync(("One", ["Bug"]), ("Two", ["Bug", "ui"]));
        NewTaskViewModel? seen = null;
        _dialogs.OnNewTask = vm =>
        {
            seen = vm;
            vm.Title = "A new task";
            vm.LabelsText = "bug, ui, fresh";
        };

        var created = await shell.OpenNewTaskAsync(project);

        Assert.True(created);
        Assert.Equal(["Bug", "ui"], seen!.KnownLabels.Select(l => l.Name).Order(StringComparer.Ordinal));

        var task = (await store.LoadAsync()).Tasks.Single(t => t.Title == "A new task");
        Assert.Equal(["Bug", "ui", "fresh"], task.Labels);
        Assert.Contains("Task 'A new task' created.", shell.StatusMessage);
        Assert.Contains("Label 'bug' as 'Bug' saved.", shell.StatusMessage);
    }

    // ---------------- The control ----------------

    private static (Window Window, LabelInputBox Box) Show(string? text = null)
    {
        var box = new LabelInputBox { KnownLabels = Known };
        var window = new Window { Width = 500, Height = 200, Content = box };
        window.Show();
        box.InputBox.Focus();
        if (text is not null)
            Type(box, text);

        return (window, box);
    }

    private static void Type(LabelInputBox box, string text)
    {
        box.InputBox.Text = text;
        box.InputBox.CaretIndex = text.Length;
    }

    /// <summary>Gives a key to the list and tells whether the list took it.</summary>
    private static bool Press(LabelInputBox box, Key key, KeyModifiers modifiers = KeyModifiers.None)
        => box.HandleKey(key, modifiers);

    [Fact]
    public async Task Typing_Opens_The_List_With_The_Fitting_Labels()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("p");

            Assert.True(box.IsSuggestionListOpen);
            Assert.Equal(["parked", "performance", "spec"], box.Suggestions.Select(s => s.Name));
            Assert.Equal(0, box.SelectedSuggestionIndex);

            window.Close();
        });
    }

    [Fact]
    public async Task A_Text_That_Fits_Nothing_Keeps_The_List_Closed()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("zzz");

            Assert.False(box.IsSuggestionListOpen);
            window.Close();
        });
    }

    [Fact]
    public async Task The_List_Stays_Closed_While_The_Box_Has_No_Focus()
    {
        await OnUiThread(() =>
        {
            var box = new LabelInputBox { KnownLabels = Known };
            var window = new Window { Width = 500, Height = 200, Content = box };
            window.Show();

            box.Text = "p";

            Assert.False(box.IsSuggestionListOpen);
            window.Close();
        });
    }

    [Fact]
    public async Task Down_And_Up_Move_Through_The_List_And_Wrap_Around()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("p");

            Assert.True(Press(box, Key.Down));
            Assert.Equal(1, box.SelectedSuggestionIndex);

            Press(box, Key.Down);
            Assert.Equal(2, box.SelectedSuggestionIndex);

            Press(box, Key.Down);
            Assert.Equal(0, box.SelectedSuggestionIndex);

            Press(box, Key.Up);
            Assert.Equal(2, box.SelectedSuggestionIndex);

            window.Close();
        });
    }

    [Fact]
    public async Task Tab_Takes_The_Suggestion_And_Puts_A_Comma_Behind_It()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("ui, p");

            Assert.True(Press(box, Key.Tab));
            Assert.Equal("ui, parked, ", box.InputBox.Text);
            Assert.Equal("ui, parked, ".Length, box.InputBox.CaretIndex);
            Assert.Equal("ui, parked, ", box.Text);
            Assert.False(box.IsSuggestionListOpen);

            window.Close();
        });
    }

    [Fact]
    public async Task Enter_Takes_The_Chosen_Suggestion()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("p");
            Press(box, Key.Down);

            Assert.True(Press(box, Key.Enter));

            Assert.Equal("performance, ", box.InputBox.Text);
            window.Close();
        });
    }

    [Fact]
    public async Task Escape_Closes_The_List_And_Keeps_The_Text()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("p");

            Assert.True(Press(box, Key.Escape));

            Assert.False(box.IsSuggestionListOpen);
            Assert.Equal("p", box.InputBox.Text);
            window.Close();
        });
    }

    [Fact]
    public async Task A_Real_Key_Press_Reaches_The_List_And_Is_Marked_As_Handled()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("ui, p");

            var args = new KeyEventArgs
            {
                Key = Key.Tab,
                RoutedEvent = InputElement.KeyDownEvent,
            };
            box.InputBox.RaiseEvent(args);

            Assert.True(args.Handled);
            Assert.Equal("ui, parked, ", box.InputBox.Text);

            window.Close();
        });
    }

    [Fact]
    public async Task Without_A_List_The_Keys_Are_Not_Taken()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("zzz");

            Assert.False(Press(box, Key.Enter));
            Assert.False(Press(box, Key.Tab));
            Assert.False(Press(box, Key.Escape));
            Assert.False(Press(box, Key.Down));

            window.Close();
        });
    }

    [Fact]
    public async Task Shift_Tab_Is_Not_Taken_By_The_List()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("p");

            Assert.False(Press(box, Key.Tab, KeyModifiers.Shift));
            Assert.Equal("p", box.InputBox.Text);

            window.Close();
        });
    }

    [Fact]
    public async Task Losing_The_Focus_Closes_The_List()
    {
        await OnUiThread(() =>
        {
            var box = new LabelInputBox { KnownLabels = Known };
            var other = new TextBox();
            var window = new Window { Width = 500, Height = 200, Content = new StackPanel { Children = { box, other } } };
            window.Show();
            box.InputBox.Focus();
            Type(box, "p");
            Assert.True(box.IsSuggestionListOpen);

            other.Focus();

            Assert.False(box.IsSuggestionListOpen);
            window.Close();
        });
    }

    [Fact]
    public async Task The_Text_Goes_Both_Ways_Between_The_Control_And_Its_Owner()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show();

            box.Text = "ui, spec";
            Assert.Equal("ui, spec", box.InputBox.Text);

            box.InputBox.Text = "ui, spec, parked";
            Assert.Equal("ui, spec, parked", box.Text);

            window.Close();
        });
    }

    [Fact]
    public async Task A_Click_On_A_Suggestion_Takes_It()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("p");
            var clicked = box.Suggestions.Single(e => e.Name == "performance");

            Assert.True(box.Choose(clicked));

            Assert.Equal("performance, ", box.InputBox.Text);
            Assert.False(box.IsSuggestionListOpen);
            window.Close();
        });
    }

    [Fact]
    public async Task A_Label_That_Is_Not_In_The_List_Cannot_Be_Chosen()
    {
        await OnUiThread(() =>
        {
            var (window, box) = Show("p");

            Assert.False(box.Choose(new LabelEntry("elsewhere", 1)));
            Assert.Equal("p", box.InputBox.Text);
            window.Close();
        });
    }

    // ---------------- Helpers ----------------

    private sealed class NewTaskDialogs : IDialogService
    {
        public Action<NewTaskViewModel>? OnNewTask { get; set; }

        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel) => Task.FromResult(false);

        public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
            => Task.FromResult<OloopProject?>(null);

        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel)
        {
            OnNewTask?.Invoke(viewModel);
            return Task.FromResult(OnNewTask is not null);
        }

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
            => Task.FromResult(ConflictChoice.Cancel);

        public Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles)
            => Task.FromResult(false);
    }
}
