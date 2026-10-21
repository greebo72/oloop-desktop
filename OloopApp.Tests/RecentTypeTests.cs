using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The new task dialog starts with the type that was used last, and the type
/// of the created task is remembered for the next one.
/// </summary>
public class RecentTypeTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "oloop-recent-" + Guid.NewGuid().ToString("N"));

    private readonly string _root;

    public RecentTypeTests()
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

    private OloopSettings Settings()
        => new()
        {
            FilePath = Path.Combine(_folder, "settings.json"),
            DataRoot = _root,
            Actor = "claude",
        };

    [Fact]
    public void The_Dialog_Starts_With_Todo_When_Nothing_Was_Picked_Yet()
    {
        var project = Project();

        var viewModel = new NewTaskViewModel(project);

        Assert.Equal(TaskType.todo, viewModel.Type);
    }

    [Fact]
    public void The_Dialog_Starts_With_The_Remembered_Type()
    {
        var project = Project();

        var viewModel = new NewTaskViewModel(project, TaskType.bug);

        Assert.Equal(TaskType.bug, viewModel.Type);
    }

    [Fact]
    public void The_Remembered_Type_Is_Also_Selected_In_The_Type_Row()
    {
        var project = Project();

        var viewModel = new NewTaskViewModel(project, TaskType.decision);

        var selected = viewModel.TypeOptions.Where(o => o.IsSelected).ToList();
        Assert.Single(selected);
        Assert.Equal(TaskType.decision, selected[0].Type);
    }

    [Fact]
    public async Task The_Created_Task_Type_Is_Remembered()
    {
        var settings = Settings();
        var shell = new MainViewModel(settings);
        var dialogs = new RecordingDialogService();
        shell.Dialogs = dialogs;

        var project = await new OloopStore(_root).CreateProjectAsync("Example");

        await shell.OpenNewTaskAsync(project);

        Assert.Equal([TaskType.todo], settings.RecentTaskTypes);
    }

    [Fact]
    public async Task The_Picked_Type_Is_Not_The_One_That_Is_Remembered()
    {
        var settings = Settings();
        var shell = new MainViewModel(settings);
        var dialogs = new RecordingDialogService();
        shell.Dialogs = dialogs;

        var project = await new OloopStore(_root).CreateProjectAsync("Example");

        // The user picks another type in the dialog.
        dialogs.OnNewTask = vm => vm.Type = TaskType.feature;

        await shell.OpenNewTaskAsync(project);

        Assert.Equal([TaskType.feature], settings.RecentTaskTypes);
    }

    [Fact]
    public async Task The_Remembered_Type_Survives_A_Restart()
    {
        var settings = Settings();
        var shell = new MainViewModel(settings);
        var dialogs = new RecordingDialogService();
        shell.Dialogs = dialogs;

        var project = await new OloopStore(_root).CreateProjectAsync("Example");
        dialogs.OnNewTask = vm => vm.Type = TaskType.question;

        await shell.OpenNewTaskAsync(project);

        // A new shell reads the settings file.
        var restarted = new MainViewModel(OloopSettings.Load(settings.FilePath));
        restarted.Dialogs = dialogs;

        await restarted.OpenNewTaskAsync(project);

        // The dialog started with the remembered type.
        Assert.Equal(TaskType.question, dialogs.NewTaskViewModel!.Type);
    }

    [Fact]
    public async Task A_Cancelled_Dialog_Changes_Nothing()
    {
        var settings = Settings();
        var shell = new MainViewModel(settings);
        var dialogs = new RecordingDialogService { CancelNewTask = true };
        shell.Dialogs = dialogs;

        var project = await new OloopStore(_root).CreateProjectAsync("Example");

        await shell.OpenNewTaskAsync(project);

        Assert.Empty(settings.RecentTaskTypes);
    }

    private static OloopProject Project() => new()
    {
        FormatVersion = 1,
        Id = "11111111-1111-4111-8111-111111111111",
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
    };

    /// <summary>A dialog service that only records what it was asked for.</summary>
    private sealed class RecordingDialogService : IDialogService
    {
        public Action<NewTaskViewModel>? OnNewTask { get; set; }

        public bool CancelNewTask { get; set; }

        public NewTaskViewModel? NewTaskViewModel { get; private set; }

        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
            => Task.FromResult(false);

        public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
            => Task.FromResult<OloopProject?>(null);

        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel)
        {
            NewTaskViewModel = viewModel;

            if (CancelNewTask)
                return Task.FromResult(false);

            // A task needs a title — the dialog would not let the user
            // save without one.
            viewModel.Title = "A new task";
            OnNewTask?.Invoke(viewModel);
            return Task.FromResult(true);
        }

        public Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles)
            => Task.FromResult(false);

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
            => Task.FromResult(ConflictChoice.Cancel);
    }
}
