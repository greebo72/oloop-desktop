using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Filters;
using OloopApp.Core.Grouping;
using OloopApp.Core.Io;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>One entry of the grouping combo box of the list header.</summary>
public sealed record GroupingOption(GroupingMode Mode, string Label);

/// <summary>One entry of the "silent since" filter of the list header.</summary>
public sealed record StaleDaysOption(string Label, double? Days);

/// <summary>
/// The left pane of the split view: the tasks of one project, filtered by
/// the search of the shell and grouped as the user wants it.
/// </summary>
public partial class TaskListViewModel : ViewModelBase
{
    private int _openCount;
    private int _totalCount;
    private bool _isEmpty;

    /// <summary>
    /// Creates the task list of a project. The settings are taken from the
    /// settings file, unless the caller passes its own.
    /// </summary>
    public TaskListViewModel(OloopProject project, MainViewModel shell, OloopSettings? settings = null)
    {
        Project = project;
        Shell = shell;

        var effective = settings ?? OloopSettings.Load();

        _grouping = ParseGrouping(effective.Grouping);
        _showClosed = effective.ShowClosed;
        _staleDays = effective.StaleDays;

        StaleDaysOptions = BuildStaleDaysOptions(effective.StaleDays);
        _selectedGrouping = GroupingOptions.FirstOrDefault(o => o.Mode == _grouping);
        _selectedStaleDays = StaleDaysOptions.FirstOrDefault(o => o.Days == _staleDays);

        shell.PropertyChanged += OnShellPropertyChanged;

        Refresh();
    }

    /// <summary>The project whose tasks are listed.</summary>
    public OloopProject Project { get; }

    /// <summary>The shell, used for the search text and the task data.</summary>
    public MainViewModel Shell { get; }

    /// <summary>The project name.</summary>
    public string Name => Project.Name;

    /// <summary>The open tasks that pass the filter.</summary>
    public int OpenCount
    {
        get => _openCount;
        private set
        {
            if (_openCount == value)
                return;
            _openCount = value;
            OnPropertyChanged();
        }
    }

    /// <summary>All tasks of the project, before the filter.</summary>
    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (_totalCount == value)
                return;
            _totalCount = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The groups of the list, one per group header.</summary>
    public ObservableCollection<TaskGroupViewModel> Groups { get; } = [];

    /// <summary>The task shown in the detail pane.</summary>
    [ObservableProperty]
    private TaskDetailViewModel? _selected;

    /// <summary>How the tasks are grouped.</summary>
    [ObservableProperty]
    private GroupingMode _grouping;

    /// <summary>Whether closed tasks are part of the list.</summary>
    [ObservableProperty]
    private bool _showClosed;

    /// <summary>The "silent since" filter in days. Null disables it.</summary>
    [ObservableProperty]
    private double? _staleDays;

    /// <summary>The grouping chosen in the header.</summary>
    [ObservableProperty]
    private GroupingOption? _selectedGrouping;

    /// <summary>The "silent since" filter chosen in the header.</summary>
    [ObservableProperty]
    private StaleDaysOption? _selectedStaleDays;

    /// <summary>Whether the filter leaves no task at all.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (_isEmpty == value)
                return;
            _isEmpty = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the project has no tasks at all — as opposed to having tasks
    /// that the filter hides.
    /// </summary>
    public bool HasNoTasks => TotalCount == 0;

    /// <summary>
    /// The title of the empty state: "No tasks yet" when the project is
    /// empty, "Nothing matches" when the filter hides the tasks.
    /// </summary>
    public string EmptyTitle => HasNoTasks ? "No tasks yet" : "Nothing matches";

    /// <summary>
    /// The text of the empty state: it says what to do about it.
    /// </summary>
    public string EmptyText => HasNoTasks
        ? "This project has no tasks yet. Create one to get started."
        : "The search and the filters hide every task of this project.";

    /// <summary>Whether a task is shown in the detail pane.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>The grouping modes offered in the header.</summary>
    public IReadOnlyList<GroupingOption> GroupingOptions { get; } =
    [
        new GroupingOption(GroupingMode.None, "No grouping"),
        new GroupingOption(GroupingMode.Created, "Created"),
        new GroupingOption(GroupingMode.Modified, "Modified"),
        new GroupingOption(GroupingMode.Type, "Type"),
        new GroupingOption(GroupingMode.Status, "Status"),
        new GroupingOption(GroupingMode.DueDate, "Due date"),
    ];

    /// <summary>The "silent since" values offered in the header.</summary>
    public IReadOnlyList<StaleDaysOption> StaleDaysOptions { get; }

    /// <summary>
    /// The files of this project that could not be read. They are never
    /// written to, and the user has to fix them in the data folder.
    /// </summary>
    public ObservableCollection<UnreadableFile> Unreadable { get; } = [];

    /// <summary>Whether this project has a file that could not be read.</summary>
    public bool HasUnreadable => Unreadable.Count > 0;

    /// <summary>The headline of the unreadable files, for example "1 file cannot be read".</summary>
    public string UnreadableSummary
    {
        get
        {
            var count = Unreadable.Count;
            return count switch
            {
                0 => "",
                1 => "1 file cannot be read",
                _ => $"{count} files cannot be read",
            };
        }
    }

    /// <summary>Re-applies the filter and the grouping to the tasks.</summary>
    public void Refresh()
    {
        var tasks = Shell.Tasks.Where(t => t.ProjectId == Project.Id).ToList();
        TotalCount = tasks.Count;

        RefreshUnreadable();

        var filter = new TaskFilter
        {
            SearchText = Shell.SearchText,
            StaleDays = StaleDays,
            ShowClosed = ShowClosed,
        };

        var filtered = filter.Apply(tasks).ToList();

        OpenCount = filtered.Count(t => !t.IsClosed);
        IsEmpty = filtered.Count == 0;

        OnPropertyChanged(nameof(HasNoTasks));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyText));

        // The detail of the selected task is rebuilt from the fresh data, so
        // it never shows the version from before the last write — except for
        // an edit that is under way: that one stays, with what the user typed.
        // A task that the filter no longer shows loses the selection.
        if (Selected is { } selected)
        {
            var fresh = filtered.FirstOrDefault(t => t.Id == selected.Task.Id);
            // A task that did not change keeps its detail as it is: the
            // reload that the watcher starts must not take the cursor away
            // from a comment that is being typed.
            Selected = fresh is null
                ? null
                : selected.Task.SameContentAs(fresh)
                    ? selected
                    : TaskDetailViewModel.Open(fresh, Project, Shell);
        }

        Groups.Clear();
        foreach (var group in TaskGrouping.Group(filtered, Grouping))
        {
            // Without a grouping there is always exactly one group, also when
            // it is empty. An empty list shows the empty state instead.
            if (group.Tasks.Count == 0)
                continue;

            Groups.Add(new TaskGroupViewModel(group.Title, group.Tasks, this));
        }
    }

    /// <summary>
    /// Shows the detail of the given row. A task with unsaved input gets it
    /// back.
    /// </summary>
    public void SelectRow(TaskRowViewModel? row)
    {
        Selected = row is null
            ? null
            : TaskDetailViewModel.Open(row.Task, Project, Shell);
    }

    /// <summary>
    /// Shows the detail of the first task of the list, the one at the top.
    /// Does nothing when the list is empty. A project that is opened from the
    /// project list starts like this, so the detail is not an empty pane.
    /// </summary>
    public void SelectFirst()
    {
        var first = Groups.SelectMany(g => g.Rows).FirstOrDefault();
        if (first is not null)
            SelectRow(first);
    }

    /// <summary>
    /// Shows the detail of the task with the given id. A closed task is
    /// shown by switching "Show closed" on, otherwise it would not be in the
    /// list. Returns false when the project has no such task.
    /// </summary>
    public bool SelectTask(string taskId)
    {
        var task = Shell.Tasks.FirstOrDefault(t => t.Id == taskId && t.ProjectId == Project.Id);
        if (task is null)
            return false;

        if (task.IsClosed)
            ShowClosed = true;

        // The "silent since" filter of the settings can hide a task that
        // was found by the search. The task wins: the filter is switched off.
        if (StaleDays is not null && !Groups.SelectMany(g => g.Rows).Any(r => r.Task.Id == taskId))
            StaleDays = null;

        var row = Groups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Task.Id == taskId);
        if (row is null)
            return false;

        SelectRow(row);
        return true;
    }

    /// <summary>
    /// Puts the given row into edit mode. The list offers the same command as
    /// the detail pane, so a task can be changed without opening it first:
    /// the row is selected and its detail edits in place.
    /// </summary>
    public async Task EditRowAsync(TaskRowViewModel? row)
    {
        if (row is null)
            return;

        SelectRow(row);
        Selected?.BeginEdit();

        await Task.CompletedTask;
    }

    /// <summary>
    /// Puts the selected task into edit mode. Does nothing when no task is
    /// selected.
    /// </summary>
    public void EditSelected() => Selected?.BeginEdit();

    partial void OnSelectedChanged(TaskDetailViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnGroupingChanged(GroupingMode value)
    {
        var option = GroupingOptions.FirstOrDefault(o => o.Mode == value);
        if (option is not null && !ReferenceEquals(option, SelectedGrouping))
            SelectedGrouping = option;

        Refresh();
    }

    partial void OnSelectedGroupingChanged(GroupingOption? value)
    {
        if (value is not null && value.Mode != Grouping)
            Grouping = value.Mode;
    }

    partial void OnShowClosedChanged(bool value)
    {
        Refresh();
    }

    partial void OnStaleDaysChanged(double? value)
    {
        var option = StaleDaysOptions.FirstOrDefault(o => o.Days == value);
        if (!ReferenceEquals(option, SelectedStaleDays))
            SelectedStaleDays = option;

        Refresh();
    }

    partial void OnSelectedStaleDaysChanged(StaleDaysOption? value)
    {
        if (value is not null && value.Days != StaleDays)
            StaleDays = value.Days;
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SearchText))
            Refresh();
    }

    /// <summary>
    /// Picks the unreadable files that belong to this project. A file belongs
    /// to the project whose folder it is in — the path of an unreadable file
    /// is the full path, and the folder of the project sits directly under
    /// the data root.
    /// </summary>
    private void RefreshUnreadable()
    {
        Unreadable.Clear();

        if (Project.FolderName.Length == 0)
            return;

        var root = (Shell.DataRoot ?? "").Trim();
        var projectFolder = root.Length > 0
            ? Path.Combine(root, Project.FolderName)
            : Project.FolderName;

        foreach (var file in Shell.Unreadable)
        {
            // The file is in the folder of the project, or in its tasks
            // folder — anywhere below it belongs to the project.
            if (IsBelow(file.Path, projectFolder))
                Unreadable.Add(file);
        }

        OnPropertyChanged(nameof(HasUnreadable));
        OnPropertyChanged(nameof(UnreadableSummary));
    }

    /// <summary>
    /// Whether a path is the folder itself or lies below it. The comparison
    /// is done on the path segments, so a project named "app" does not match
    /// the project "app-2".
    /// </summary>
    private static bool IsBelow(string path, string folder)
    {
        var normalized = Path.GetFullPath(path);
        var root = Path.GetFullPath(folder);

        if (normalized.Equals(root, StringComparison.Ordinal))
            return true;

        return normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static GroupingMode ParseGrouping(string? name)
        => Enum.TryParse<GroupingMode>(name, ignoreCase: true, out var mode)
            ? mode
            : GroupingMode.None;

    private static IReadOnlyList<StaleDaysOption> BuildStaleDaysOptions(double? current)
    {
        var options = new List<StaleDaysOption> { new("Any time", null) };

        foreach (var days in new double?[] { 3, 7, 14, 30, 60, 90, 180 })
            options.Add(new StaleDaysOption($"{days} days", days));

        if (current is { } value && !options.Any(o => o.Days == value))
            options.Add(new StaleDaysOption($"{value} days", value));

        return options;
    }
}
