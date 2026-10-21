using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The keyboard shortcuts of the shell.
///
/// The shortcuts that would type something (Ctrl+N, Ctrl+E, Ctrl+,) must not
/// fire while a text field has the focus — otherwise typing in the search box
/// or in the comment field would open a dialog.
/// </summary>
public class ShortcutTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShortcutTests).Assembly);

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    private static OloopProject Project() => new()
    {
        FormatVersion = 1,
        Id = "11111111-1111-4111-8111-111111111111",
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = "example-project",
    };

    private static OloopTask SampleTask() => new()
    {
        FormatVersion = 1,
        Id = "22222222-2222-4222-8222-222222222222",
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.todo,
        Title = "Write the docs",
        Status = TaskStatus.open,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "timo",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "timo",
        Labels = [],
    };

    /// <summary>
    /// Sends a key to the window. The key goes through the whole route, so the
    /// window sees it like a real key press.
    /// </summary>
    private static void Press(MainWindow window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs
        {
            Key = key,
            KeyModifiers = modifiers,
            RoutedEvent = InputElement.KeyDownEvent,
        };

        window.RaiseEvent(args);
    }

    // ---------------- Ctrl+F ----------------

    [Fact]
    public async Task Ctrl_F_Focuses_The_Search_Field()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            Press(window, Key.F, KeyModifiers.Control);

            var box = window.FindControl<TextBox>("SearchBox");
            Assert.NotNull(box);
            Assert.True(box!.IsFocused);

            window.Close();
        });
    }

    // ---------------- Escape ----------------

    [Fact]
    public async Task Escape_Clears_The_Search()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { SearchText = "docs" };
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            Press(window, Key.Escape);

            Assert.Equal("", shell.SearchText);

            window.Close();
        });
    }

    [Fact]
    public async Task Escape_In_The_Search_Field_Clears_It_And_Stays_There()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { SearchText = "docs" };
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            var box = window.FindControl<TextBox>("SearchBox")!;
            box.Focus();

            Press(window, Key.Escape);

            Assert.Equal("", shell.SearchText);
            Assert.Null(shell.Split);

            window.Close();
        });
    }

    [Fact]
    public async Task Escape_Goes_Back_To_The_Dashboard()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [SampleTask()],
                Unreadable = [],
            });
            shell.Tasks.Add(SampleTask());

            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);
            Assert.NotNull(shell.Split);

            Press(window, Key.Escape);

            Assert.Null(shell.Split);
            Assert.True(shell.IsDashboard);

            window.Close();
        });
    }

    // ---------------- Ctrl+N ----------------

    [Fact]
    public async Task Ctrl_N_Opens_The_New_Task_Dialog()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [SampleTask()],
                Unreadable = [],
            });
            shell.Tasks.Add(SampleTask());
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

            var dialogs = new CountingDialogService();
            shell.Dialogs = dialogs;

            Press(window, Key.N, KeyModifiers.Control);

            Assert.Equal(1, dialogs.NewTaskCalls);

            window.Close();
        });
    }

    [Fact]
    public async Task Ctrl_N_Does_Nothing_In_A_Text_Field()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [SampleTask()],
                Unreadable = [],
            });
            shell.Tasks.Add(SampleTask());
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

            var dialogs = new CountingDialogService();
            shell.Dialogs = dialogs;

            var box = window.FindControl<TextBox>("SearchBox")!;
            box.Focus();

            Press(window, Key.N, KeyModifiers.Control);

            Assert.Equal(0, dialogs.NewTaskCalls);

            window.Close();
        });
    }

    // ---------------- Ctrl+E ----------------

    [Fact]
    public async Task Ctrl_E_Opens_The_Edit_Dialog_Of_The_Selected_Task()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            var task = SampleTask();
            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);
            shell.Split!.TaskList.SelectRow(shell.Split.TaskList.Groups[0].Rows[0]);

            var dialogs = new CountingDialogService();
            shell.Dialogs = dialogs;

            Press(window, Key.E, KeyModifiers.Control);

            Assert.Equal(1, dialogs.EditTaskCalls);

            window.Close();
        });
    }

    [Fact]
    public async Task Ctrl_E_Does_Nothing_Without_A_Selected_Task()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [SampleTask()],
                Unreadable = [],
            });
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

            var dialogs = new CountingDialogService();
            shell.Dialogs = dialogs;

            Press(window, Key.E, KeyModifiers.Control);

            Assert.Equal(0, dialogs.EditTaskCalls);

            window.Close();
        });
    }

    // ---------------- Ctrl+, ----------------

    [Fact]
    public async Task Ctrl_Comma_Opens_The_Settings()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();
            window.UpdateLayout();

            var dialogs = new CountingDialogService();
            shell.Dialogs = dialogs;

            Press(window, Key.OemComma, KeyModifiers.Control);

            Assert.Equal(1, dialogs.SettingsCalls);

            window.Close();
        });
    }

    // ---------------- F5 ----------------

    [Fact]
    public async Task F5_Reloads_The_Data_Root()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "oloop-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new Core.Io.OloopStore(root);
            var project = await store.CreateProjectAsync("Example Project");
            await store.CreateTaskAsync(
                project, TaskType.todo, "Write the docs", "", [], "claude");

            var shell = new MainViewModel { DataRoot = root, Actor = "claude" };

            await OnUiThread(() =>
            {
                var window = new MainWindow { DataContext = shell };
                window.Show();
                window.UpdateLayout();

                Assert.Empty(shell.Tasks);

                Press(window, Key.F5);

                window.Close();
            });

            // The refresh runs in the background. Its own task cannot be
            // waited for — the command clears it as soon as the refresh is
            // done, and a small data root is read before the test even looks
            // at it. So the test waits for the effect instead.
            //
            // The collection is read on the UI thread, because that is where
            // the refresh puts the tasks into it. A reader on another thread
            // can see the collection while it is being filled.
            var titles = await WaitForTaskTitles(shell, 1);

            Assert.Equal(["Write the docs"], titles);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Waits until the shell has the given number of tasks and returns their
    /// titles. The collection is read on the UI thread, because that is where
    /// the refresh fills it.
    /// </summary>
    private static async Task<List<string>> WaitForTaskTitles(
        MainViewModel shell, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var titles = new List<string>();

        while (DateTime.UtcNow < deadline)
        {
            await OnUiThread(() =>
            {
                titles.Clear();

                foreach (var task in shell.Tasks)
                    titles.Add(task.Title);
            });

            if (titles.Count == count)
                return titles;

            await Task.Delay(20);
        }

        Assert.Fail($"the shell has {titles.Count} tasks, {count} were expected");
        return titles;
    }

    // ---------------- The dialog service ----------------

    /// <summary>
    /// Counts how often a dialog was asked for. The dialogs are not opened —
    /// the tests only care about the shortcut reaching the shell.
    /// </summary>
    private sealed class CountingDialogService : IDialogService
    {
        public int SettingsCalls { get; private set; }
        public int NewTaskCalls { get; private set; }
        public int EditTaskCalls { get; private set; }

        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
        {
            SettingsCalls++;
            return Task.FromResult(false);
        }

        public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
            => Task.FromResult<OloopProject?>(null);

        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel)
        {
            NewTaskCalls++;
            return Task.FromResult(false);
        }

        public Task<bool> ShowEditTaskAsync(EditTaskViewModel viewModel)
        {
            EditTaskCalls++;
            return Task.FromResult(false);
        }

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
            => Task.FromResult(ConflictChoice.Cancel);
    }
}
