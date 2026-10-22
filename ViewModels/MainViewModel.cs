using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Io;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The shell of the app: the top bar, the navigation between the dashboard
/// and a project, the settings, the search and every write to the data root.
///
/// The shell owns the write path. The dialogs only collect the input, the
/// shell performs the write and handles a refused write (a conflict) by
/// asking the user what to do.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const string NoDataRoot = "No data folder set. Open the settings to choose one.";
    private const string NoActorGiven = "Nothing was written: the actor name is still empty.";
    private const string RootChanged = "The data folder changed, so nothing was written. Please try again.";

    private readonly OloopSettings _settings;
    private OloopStore? _store;
    private string? _storeRoot;

    /// <summary>
    /// Creates the shell. The dialog service is set by the app after the
    /// window exists; until then the commands do nothing.
    /// </summary>
    public MainViewModel()
        : this(OloopSettings.Load())
    {
    }

    /// <summary>
    /// Creates the shell with the given settings. The app uses the settings
    /// of the user; a test can hand in its own.
    /// </summary>
    public MainViewModel(OloopSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _dataRoot = _settings.ResolveDataRoot() ?? "";
        _actor = _settings.ResolveActor();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenProjectCommand = new RelayCommand<ProjectCardViewModel>(OpenProject);
        BackToDashboardCommand = new RelayCommand(BackToDashboard);
        OpenSilentCommand = new RelayCommand(OpenSilent);
        ToggleSilentCommand = new RelayCommand(ToggleSilent);

        Dashboard = new DashboardViewModel();
        // The project cards on the dashboard open the project through this
        // command — without it they would be dead.
        Dashboard.OpenProjectCommand = OpenProjectCommand;

        OpenTaskCommand = new RelayCommand<TaskRowViewModel>(OpenTask);
        Dashboard.OpenTaskCommand = OpenTaskCommand;
        Dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.SearchTasks))
                OnPropertyChanged(nameof(SearchPlaceholder));
        };
        ShowProjectSearchCommand = new RelayCommand(() => Dashboard.SearchTasks = false);
        ShowTaskSearchCommand = new RelayCommand(() => Dashboard.SearchTasks = true);

        // Without a data folder the dashboard shows the empty state right
        // away — before the first refresh, too.
        Dashboard.HasNoDataRoot = _dataRoot.Length == 0;
        Dashboard.IsEmpty = _dataRoot.Length == 0;

        SettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        NewProjectCommand = new AsyncRelayCommand(OpenNewProjectAsync);
        NewTaskCommand = new AsyncRelayCommand(OpenNewTaskAsync);
        EditTaskCommand = new AsyncRelayCommand(EditSelectedTaskAsync);
        ClearSearchCommand = new RelayCommand(ClearSearch);
    }

    /// <summary>
    /// The placeholder of the search field. On the dashboard it follows the
    /// mode of the search, in a project the search filters the tasks.
    /// </summary>
    public string SearchPlaceholder
        => !IsDashboard || Dashboard.SearchTasks ? "Find tasks …" : "Find projects …";

    /// <summary>Clears the search field.</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>
    /// Empties the search. The dashboard and the task list follow, because
    /// the search text is shared.
    /// </summary>
    private void ClearSearch()
    {
        SearchText = "";
    }

    /// <summary>
    /// The dialogs of the app. The app replaces this with the real service
    /// once the window exists.
    /// </summary>
    public IDialogService Dialogs { get; set; } = NullDialogService.Instance;

    /// <summary>The dashboard with the project cards.</summary>
    public DashboardViewModel Dashboard { get; }

    /// <summary>
    /// The "silent since" view: the tasks of all projects that have been
    /// quiet for a long time. Null when the view is not open.
    /// </summary>
    [ObservableProperty]
    private SilentTasksViewModel? _silent;

    /// <summary>The task list and detail of the open project. Null on the dashboard.</summary>
    [ObservableProperty]
    private ProjectSplitViewModel? _split;

    /// <summary>The data root the app reads from.</summary>
    [ObservableProperty]
    private string _dataRoot = "";

    /// <summary>The actor written into createdBy/updatedBy.</summary>
    [ObservableProperty]
    private string _actor = "";

    /// <summary>The search text of the top bar.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>A short status line under the top bar.</summary>
    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>Whether the app is loading.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>All tasks of the loaded data root, kept for the split view.</summary>
    public ObservableCollection<OloopTask> Tasks { get; } = [];

    /// <summary>All projects of the loaded data root.</summary>
    public ObservableCollection<OloopProject> Projects { get; } = [];

    /// <summary>
    /// The files of the loaded data root that could not be read. The
    /// dashboard shows all of them, the task list of a project only the ones
    /// of that project.
    /// </summary>
    public ObservableCollection<UnreadableFile> Unreadable { get; } = [];

    /// <summary>Whether the dashboard is shown (no project open).</summary>
    public bool IsDashboard => Split is null && Silent is null;

    /// <summary>Whether the "silent since" view is shown.</summary>
    public bool IsSilent => Silent is not null;

    /// <summary>
    /// Whether something is open next to the dashboard: a project or the
    /// "silent since" view. The back button is only shown then.
    /// </summary>
    public bool HasSubView => Split is not null || Silent is not null;

    /// <summary>Reloads the data root.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Opens a project in the split view.</summary>
    public RelayCommand<ProjectCardViewModel> OpenProjectCommand { get; }

    /// <summary>Returns to the dashboard.</summary>
    public RelayCommand BackToDashboardCommand { get; }

    /// <summary>
    /// Opens the "silent since" view: the tasks of all projects that have
    /// been quiet for a long time.
    /// </summary>
    public RelayCommand OpenSilentCommand { get; }

    /// <summary>
    /// Opens the "silent since" view, or closes it when it is already open.
    /// The button in the top bar is a toggle.
    /// </summary>
    public RelayCommand ToggleSilentCommand { get; }

    /// <summary>Opens the settings dialog.</summary>
    public AsyncRelayCommand SettingsCommand { get; }

    /// <summary>
    /// The size of the UI from the settings, as a factor. The app hands it to
    /// <see cref="UiScale"/> at start; it is not applied in the constructor,
    /// because the scale is global and a test must not change it by creating
    /// a shell.
    /// </summary>
    public double UiScaleFactor => _settings.UiScaleFactor;

    /// <summary>Opens the new project dialog.</summary>
    public AsyncRelayCommand NewProjectCommand { get; }

    /// <summary>Opens the new task dialog for the open project.</summary>
    public AsyncRelayCommand NewTaskCommand { get; }

    /// <summary>
    /// Opens the edit dialog for the selected task. Does nothing when no task
    /// is selected.
    /// </summary>
    public AsyncRelayCommand EditTaskCommand { get; }

    /// <summary>
    /// The task that is currently shown in the detail pane, or null when no
    /// project is open or no task is selected.
    /// </summary>
    public OloopTask? SelectedTask => Split?.TaskList.Selected?.Task;

    /// <summary>
    /// Loads the data root. A missing root is not an error — the app shows an
    /// empty state and the settings dialog.
    /// </summary>
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var root = DataRoot?.Trim() ?? "";
            if (root.Length == 0)
            {
                StopWatching();
                // The empty card of the dashboard says it — a second line
                // with the same words would only be noise.
                StatusMessage = "";
                Dashboard.HasNoDataRoot = true;
                Dashboard.Update(OloopSnapshot.Empty);
                Projects.Clear();
                Tasks.Clear();
                Unreadable.Clear();
                Silent?.Refresh();
                return;
            }

            if (!Directory.Exists(root))
            {
                StopWatching();
                StatusMessage = $"The data folder '{root}' does not exist.";
                Dashboard.HasNoDataRoot = true;
                Dashboard.Update(OloopSnapshot.Empty);
                Projects.Clear();
                Tasks.Clear();
                Unreadable.Clear();
                Silent?.Refresh();
                return;
            }

            var store = new OloopStore(root);

            // Taken before the read: a file that changes while the folder is
            // read makes the next check differ, so the change is not lost.
            var fingerprint = store.Fingerprint();
            var snapshot = await store.LoadAsync();
            _fingerprint = fingerprint;
            EnsureWatching(root);

            DataReloading?.Invoke();

            Projects.Clear();
            foreach (var project in snapshot.Projects)
                Projects.Add(project);

            Tasks.Clear();
            foreach (var task in snapshot.Tasks)
                Tasks.Add(task);

            Unreadable.Clear();
            foreach (var file in snapshot.Unreadable)
                Unreadable.Add(file);

            Dashboard.Update(snapshot);
            Dashboard.HasNoDataRoot = false;

            StatusMessage = snapshot.Unreadable.Count == 0
                ? ""
                : $"{snapshot.Unreadable.Count} file(s) could not be read.";

            PruneDrafts();

            Split?.Refresh();
            Silent?.Refresh();

            DataReloaded?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not read the data folder: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------- Watching the data folder ----------------

    private DataFolderWatcher? _watcher;
    private string? _fingerprint;
    private SynchronizationContext? _uiContext;

    /// <summary>
    /// Whether the app watches the data folder and reloads by itself when
    /// something in it changes. The app switches it on; tests leave it off
    /// unless they test the watching.
    /// </summary>
    public bool WatchDataFolder { get; set; }

    /// <summary>The quiet time after the last file event before the view reloads.</summary>
    public TimeSpan WatchDebounce { get; set; } = DataFolderWatcher.DefaultDebounce;

    /// <summary>
    /// Raised before a reload replaces the data and after it did. A view uses
    /// the pair to keep what the user sees, for example the scroll position.
    /// </summary>
    public event Action? DataReloading;

    /// <inheritdoc cref="DataReloading" />
    public event Action? DataReloaded;

    /// <summary>
    /// Starts watching the folder when the app watches at all and the folder
    /// is not watched yet. A changed folder gets a new watch.
    /// </summary>
    private void EnsureWatching(string root)
    {
        if (!WatchDataFolder)
            return;

        if (_watcher is not null
            && string.Equals(_watcher.Folder, root, StringComparison.Ordinal))
        {
            return;
        }

        StopWatching();

        // The events come on a thread pool thread. The reload touches what the
        // window shows, so it goes back to the thread that started the watch.
        _uiContext = SynchronizationContext.Current;

        try
        {
            var watcher = new DataFolderWatcher(root, WatchDebounce);
            watcher.Changed += OnWatcherChanged;
            watcher.Failed += OnWatcherFailed;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or PlatformNotSupportedException)
        {
            // No workaround such as polling: the problem is shown and reported.
            StatusMessage = $"The data folder cannot be watched: {ex.Message} Use Reload (F5) to see changes.";
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _fingerprint = null;
    }

    private void OnWatcherChanged()
    {
        if (_uiContext is { } context)
            context.Post(_ => _ = ReloadIfChangedAsync(), null);
        else
            _ = ReloadIfChangedAsync();
    }

    private void OnWatcherFailed(Exception error)
    {
        void Show() => StatusMessage =
            $"Watching the data folder failed: {error.Message} Some changes may not show up, use Reload (F5).";

        if (_uiContext is { } context)
            context.Post(_ => Show(), null);
        else
            Show();
    }

    /// <summary>
    /// Reloads when the folder differs from what was loaded last. The app's
    /// own writes have been reloaded already, so a file event that follows one
    /// finds the same state and does nothing.
    /// </summary>
    public async Task ReloadIfChangedAsync()
    {
        var root = DataRoot?.Trim() ?? "";
        if (root.Length == 0 || !Directory.Exists(root))
            return;

        if (new OloopStore(root).Fingerprint() == _fingerprint)
            return;

        await RefreshAsync();
    }

    /// <summary>Stops watching the data folder.</summary>
    public void Dispose()
    {
        StopWatching();
        GC.SuppressFinalize(this);
    }

    // ---------------- Dialogs ----------------

    /// <summary>
    /// Opens the settings. When the user saves them, the new data root and
    /// actor are taken over and the data root is reloaded.
    /// </summary>
    public async Task OpenSettingsAsync()
    {
        var viewModel = new SettingsViewModel(_settings);
        if (!await Dialogs.ShowSettingsAsync(viewModel))
            return;

        ApplySettings();

        await RefreshAsync();
    }

    /// <summary>
    /// Takes the values of the settings over into the shell.
    /// </summary>
    private void ApplySettings()
    {
        // The dialog wrote the values into the settings instance.
        DataRoot = _settings.ResolveDataRoot() ?? "";
        Actor = _settings.ResolveActor();
        UiScale.Factor = _settings.UiScaleFactor;
    }

    /// <summary>
    /// The actor for a write. When there is none, the settings open and the
    /// user is asked for the name; the write only goes ahead when a name was
    /// entered (app-brief: "Until it is set, the app asks for it before the
    /// first write and does not write anything without it").
    ///
    /// Returns null when there is still no actor — then nothing is written.
    /// </summary>
    private async Task<string?> EnsureActorAsync()
    {
        var actor = Actor?.Trim() ?? "";
        if (actor.Length > 0)
            return actor;

        var rootBefore = DataRoot?.Trim() ?? "";

        var viewModel = new SettingsViewModel(_settings);
        if (!await Dialogs.ShowSettingsAsync(viewModel))
        {
            StatusMessage = NoActorGiven;
            return null;
        }

        ApplySettings();

        // The settings can also change the data folder. A write against the
        // old folder would land in the wrong place, so it is refused and
        // the new folder is loaded.
        if ((DataRoot?.Trim() ?? "") != rootBefore)
        {
            await RefreshAsync();
            StatusMessage = RootChanged;
            return null;
        }

        actor = Actor?.Trim() ?? "";
        if (actor.Length == 0)
        {
            StatusMessage = NoActorGiven;
            return null;
        }

        return actor;
    }

    /// <summary>
    /// Opens the new project dialog and adds the created project to the
    /// dashboard.
    /// </summary>
    public async Task OpenNewProjectAsync()
    {
        var store = Store;
        if (store is null)
        {
            StatusMessage = NoDataRoot;
            return;
        }

        var viewModel = new NewProjectViewModel(store, Actor?.Trim() ?? "");
        var project = await Dialogs.ShowNewProjectAsync(viewModel);
        if (project is null)
            return;

        await RefreshAsync();
    }

    /// <summary>
    /// Opens the new task dialog for the open project and writes the task.
    /// </summary>
    public async Task OpenNewTaskAsync()
    {
        if (Split is null)
            return;

        await OpenNewTaskAsync(Split.Project);
    }

    /// <summary>
    /// Opens the new task dialog for the given project and writes the task.
    /// </summary>
    public async Task<bool> OpenNewTaskAsync(OloopProject project)
    {
        // The name is asked for before the dialog opens: the user should not
        // type a whole task first and only then be asked who they are.
        if (await EnsureActorAsync() is null)
            return false;

        // The dialog starts with the type the user picked last.
        var viewModel = new NewTaskViewModel(project, PreferredType);
        if (!await Dialogs.ShowNewTaskAsync(viewModel))
            return false;

        var created = await CreateTaskAsync(
            project, viewModel.Type, viewModel.TrimmedTitle,
            viewModel.TrimmedDescription, viewModel.Labels, viewModel.DueDateStorage);

        if (created)
        {
            // The type of the created task is remembered for the next one.
            RememberType(viewModel.Type);
            StatusMessage = $"Task '{viewModel.TrimmedTitle}' created.";
        }

        return created;
    }

    /// <summary>
    /// The task type the new task dialog starts with: the type that was used
    /// last. Null when nothing was picked yet.
    /// </summary>
    private TaskType? PreferredType
    {
        get
        {
            var recent = _settings.RecentTaskTypes;
            return recent.Count > 0 ? recent[0] : null;
        }
    }

    /// <summary>
    /// Remembers the type of the last created task and writes the settings
    /// file. A settings file that cannot be written does not stop the task —
    /// the task is already on disk.
    /// </summary>
    private void RememberType(TaskType type)
    {
        _settings.RememberType(type);

        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            // The remembered type is a convenience, not a promise.
        }
    }

    /// <summary>
    /// Puts the task that is shown in the detail pane into edit mode. The
    /// detail edits in place, there is no dialog.
    /// </summary>
    public Task EditSelectedTaskAsync()
    {
        if (Split is not null)
            Split.TaskList.EditSelected();
        else
            Silent?.EditSelected();

        return Task.CompletedTask;
    }

    // ---------------- Unsaved changes ----------------

    private readonly Dictionary<string, TaskDetailViewModel> _drafts = [];

    /// <summary>
    /// Whether a detail pane holds input that is not written yet: an edit
    /// that changed something, or a comment that was typed but not added.
    /// </summary>
    public bool HasUnsavedChanges => _drafts.Count > 0;

    /// <summary>
    /// The titles of the tasks with unsaved changes, for the question the
    /// user is asked before the window closes.
    /// </summary>
    public IReadOnlyList<string> UnsavedTaskTitles
        => _drafts.Values.Select(d => d.Task.Title).OrderBy(t => t, StringComparer.CurrentCulture).ToList();

    /// <summary>
    /// The detail with unsaved changes for a task, or null. The drafts live
    /// in the shell, so they survive a change of the selection, of the
    /// project and of the view.
    /// </summary>
    public TaskDetailViewModel? TryGetDraft(string taskId)
        => _drafts.GetValueOrDefault(taskId);

    /// <summary>Remembers the detail as the draft of its task.</summary>
    internal void SetDraft(string taskId, TaskDetailViewModel detail)
    {
        _drafts[taskId] = detail;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>
    /// Forgets the draft of a task — but only when the given detail is the
    /// draft. A newer detail of the same task keeps its draft.
    /// </summary>
    internal void ClearDraft(string taskId, TaskDetailViewModel detail)
    {
        if (_drafts.TryGetValue(taskId, out var current) && ReferenceEquals(current, detail))
        {
            _drafts.Remove(taskId);
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    /// <summary>
    /// Drops the drafts of tasks that are no longer on disk, and says so. A
    /// draft for a task that is gone cannot be written anyway.
    /// </summary>
    private void PruneDrafts()
    {
        var gone = _drafts.Keys.Where(id => Tasks.All(t => t.Id != id)).ToList();
        if (gone.Count == 0)
            return;

        foreach (var id in gone)
            _drafts.Remove(id);

        OnPropertyChanged(nameof(HasUnsavedChanges));
        StatusMessage = gone.Count == 1
            ? "A task with unsaved changes is no longer in the data folder."
            : $"{gone.Count} tasks with unsaved changes are no longer in the data folder.";
    }

    // ---------------- Writes ----------------

    /// <summary>Creates a task in the given project.</summary>
    public async Task<bool> CreateTaskAsync(
        OloopProject project,
        TaskType type,
        string title,
        string description,
        IReadOnlyList<string> labels,
        string? dueDate = null)
    {
        var store = Store;
        if (store is null)
        {
            StatusMessage = NoDataRoot;
            return false;
        }

        var actor = await EnsureActorAsync();
        if (actor is null)
            return false;

        try
        {
            await store.CreateTaskAsync(project, type, title, description, labels, actor,
                dueDate: dueDate);
            await RefreshAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or OloopFormatException)
        {
            StatusMessage = $"The change could not be written: {ex.Message}";
            return false;
        }
    }

    /// <summary>Changes title, description and labels of a task.</summary>
    public async Task<bool> EditTaskAsync(
        OloopTask task,
        OloopProject project,
        string title,
        string description,
        IReadOnlyList<string> labels,
        bool setDueDate = false,
        string? dueDate = null)
    {
        var store = Store;
        if (store is null)
        {
            StatusMessage = NoDataRoot;
            return false;
        }

        var actor = await EnsureActorAsync();
        if (actor is null)
            return false;

        return await MutateAsync(store, task, baseTask =>
            store.EditTaskAsync(baseTask, project, title, description, labels, actor,
                setDueDate: setDueDate, dueDate: dueDate));
    }

    /// <summary>
    /// Changes title, description and labels of a task for the edit mode of
    /// the detail pane. The outcome tells the pane whether to keep what the
    /// user typed: only "Reload" in the conflict dialog throws it away.
    /// </summary>
    public async Task<WriteOutcome> SaveTaskEditAsync(
        OloopTask task,
        OloopProject project,
        string title,
        string description,
        IReadOnlyList<string> labels,
        bool setDueDate = false,
        string? dueDate = null)
    {
        var store = Store;
        if (store is null)
        {
            StatusMessage = NoDataRoot;
            return WriteOutcome.NotSaved;
        }

        var actor = await EnsureActorAsync();
        if (actor is null)
            return WriteOutcome.NotSaved;

        var outcome = await MutateOutcomeAsync(store, task, baseTask =>
            store.EditTaskAsync(baseTask, project, title, description, labels, actor,
                setDueDate: setDueDate, dueDate: dueDate));

        if (outcome == WriteOutcome.Saved)
            StatusMessage = $"Task '{title}' saved.";

        return outcome;
    }

    /// <summary>Closes or reopens a task.</summary>
    public async Task<bool> SetTaskClosedAsync(OloopTask task, OloopProject project, bool closed)
    {
        var store = Store;
        if (store is null)
        {
            StatusMessage = NoDataRoot;
            return false;
        }

        var actor = await EnsureActorAsync();
        if (actor is null)
            return false;

        return await MutateAsync(store, task, baseTask =>
            store.SetStatusAsync(baseTask, project, closed, actor));
    }

    /// <summary>Appends a comment to a task.</summary>
    public async Task<bool> AddCommentAsync(OloopTask task, OloopProject project, string body)
    {
        var store = Store;
        if (store is null)
        {
            StatusMessage = NoDataRoot;
            return false;
        }

        var actor = await EnsureActorAsync();
        if (actor is null)
            return false;

        return await MutateAsync(store, task, baseTask =>
            store.AddCommentAsync(baseTask, project, body, actor));
    }

    /// <summary>
    /// Changes an existing task and reloads the data root afterwards, so the
    /// list shows what is on disk.
    ///
    /// A refused write (the file changed on disk in the meantime) is handed to
    /// the conflict dialog: the user reloads, overwrites or cancels.
    /// </summary>
    private async Task<bool> MutateAsync(
        OloopStore store,
        OloopTask task,
        Func<OloopTask, Task<OloopTask>> write)
        => await MutateOutcomeAsync(store, task, write) == WriteOutcome.Saved;

    private async Task<WriteOutcome> MutateOutcomeAsync(
        OloopStore store,
        OloopTask task,
        Func<OloopTask, Task<OloopTask>> write)
    {
        try
        {
            await write(task);
            await RefreshAsync();
            return WriteOutcome.Saved;
        }
        catch (OloopConflictException conflict)
        {
            return await HandleConflictAsync(store, task, conflict, write);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or OloopFormatException)
        {
            StatusMessage = $"The change could not be written: {ex.Message}";
            return WriteOutcome.NotSaved;
        }
    }

    /// <summary>
    /// Asks the user what to do when the store refused a write because the
    /// file changed on disk.
    /// </summary>
    private async Task<WriteOutcome> HandleConflictAsync(
        OloopStore store,
        OloopTask task,
        OloopConflictException conflict,
        Func<OloopTask, Task<OloopTask>> write)
    {
        var choice = await Dialogs.ShowConflictAsync(new ConflictViewModel(task, conflict));

        switch (choice)
        {
            case ConflictChoice.Reload:
                await RefreshAsync();
                StatusMessage = "The task was reloaded from disk.";
                return WriteOutcome.Discarded;

            case ConflictChoice.Overwrite:
                return await OverwriteAsync(store, task, write)
                    ? WriteOutcome.Saved
                    : WriteOutcome.NotSaved;

            default:
                return WriteOutcome.NotSaved;
        }
    }

    /// <summary>
    /// Writes the change over the version on disk: the file is read again and
    /// the change is applied on top of that version.
    /// </summary>
    private async Task<bool> OverwriteAsync(
        OloopStore store,
        OloopTask task,
        Func<OloopTask, Task<OloopTask>> write)
    {
        try
        {
            var snapshot = await store.LoadAsync();
            var onDisk = snapshot.Tasks.FirstOrDefault(t => t.Id == task.Id);
            if (onDisk is null)
            {
                StatusMessage = "The task is no longer in the data folder.";
                await RefreshAsync();
                return false;
            }

            // The change is applied on the version from disk, so the write
            // does not trip over the changed updatedAt again.
            await write(onDisk);
            await RefreshAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or OloopFormatException)
        {
            StatusMessage = $"The change could not be written: {ex.Message}";
            return false;
        }
    }

    // ---------------- Helpers ----------------

    /// <summary>
    /// The store for the current data root. Null when no data root is set.
    /// </summary>
    private OloopStore? Store
    {
        get
        {
            var root = DataRoot?.Trim() ?? "";
            if (root.Length == 0)
                return null;

            if (_store is null || _storeRoot != root)
            {
                _store = new OloopStore(root);
                _storeRoot = root;
            }

            return _store;
        }
    }

    /// <summary>
    /// The store of the current data root, also when there is none. The views
    /// use it to look up files that belong to a task, like its image.
    /// </summary>
    public OloopStore? StoreOrNull
    {
        get
        {
            var root = DataRoot?.Trim() ?? "";
            return root.Length == 0 ? null : Store;
        }
    }

    private void OpenProject(ProjectCardViewModel? card)
    {
        if (card is null)
            return;

        // Only one view at a time: the project and the "silent since" view
        // share the right pane.
        Silent = null;
        Split = new ProjectSplitViewModel(card.Project, this);

        // A project opens with its first task selected, if it has one.
        Split.TaskList.SelectFirst();
    }

    /// <summary>Opens the task of a search result in its project.</summary>
    public RelayCommand<TaskRowViewModel> OpenTaskCommand { get; }

    /// <summary>Makes the search of the dashboard look for projects.</summary>
    public RelayCommand ShowProjectSearchCommand { get; }

    /// <summary>Makes the search of the dashboard look for tasks of all projects.</summary>
    public RelayCommand ShowTaskSearchCommand { get; }

    /// <summary>
    /// Opens the project of a task found by the search, with the task
    /// selected in the detail. The search text is cleared first: the task
    /// list of the project filters by it too, and a match in the description
    /// would hide the very task the user clicked.
    /// </summary>
    private void OpenTask(TaskRowViewModel? row)
    {
        if (row is null)
            return;

        var project = Projects.FirstOrDefault(p => p.Id == row.Task.ProjectId);
        if (project is null)
            return;

        SearchText = "";
        Silent = null;
        Split = new ProjectSplitViewModel(project, this);

        // A task that cannot be selected (it was removed in the meantime)
        // leaves the project open with its first task, like any project.
        if (!Split.TaskList.SelectTask(row.Task.Id))
            Split.TaskList.SelectFirst();
    }

    private void BackToDashboard()
    {
        Split = null;
        Silent = null;
    }

    /// <summary>
    /// Opens the "silent since" view. The project view is closed — only one
    /// of them is on the screen.
    /// </summary>
    private void OpenSilent()
    {
        Split = null;
        Silent = new SilentTasksViewModel(this);
    }

    /// <summary>
    /// Opens the "silent since" view, or closes it when it is already open.
    /// </summary>
    private void ToggleSilent()
    {
        if (Silent is not null)
        {
            BackToDashboard();
            return;
        }

        OpenSilent();
    }

    partial void OnSplitChanged(ProjectSplitViewModel? value)
        => RaiseViewStateChanged();

    partial void OnSilentChanged(SilentTasksViewModel? value)
        => RaiseViewStateChanged();

    /// <summary>
    /// Raises the properties that say which view is on the screen.
    /// </summary>
    private void RaiseViewStateChanged()
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(IsSilent));
        OnPropertyChanged(nameof(HasSubView));
    }

    partial void OnSearchTextChanged(string value)
        => Dashboard.SearchText = value;

    partial void OnDataRootChanged(string value)
        => Dashboard.HasNoDataRoot = string.IsNullOrWhiteSpace(value);
}
