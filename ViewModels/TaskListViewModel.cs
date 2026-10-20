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
    ];

    /// <summary>The "silent since" values offered in the header.</summary>
    public IReadOnlyList<StaleDaysOption> StaleDaysOptions { get; }

    /// <summary>Re-applies the filter and the grouping to the tasks.</summary>
    public void Refresh()
    {
        var tasks = Shell.Tasks.Where(t => t.ProjectId == Project.Id).ToList();
        TotalCount = tasks.Count;

        var filter = new TaskFilter
        {
            SearchText = Shell.SearchText,
            StaleDays = StaleDays,
            ShowClosed = ShowClosed,
        };

        var filtered = filter.Apply(tasks).ToList();

        OpenCount = filtered.Count(t => !t.IsClosed);
        IsEmpty = filtered.Count == 0;

        if (Selected is { } selected && filtered.All(t => t.Id != selected.Task.Id))
            Selected = null;

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

    /// <summary>Shows the detail of the given row.</summary>
    public void SelectRow(TaskRowViewModel? row)
    {
        Selected = row is null
            ? null
            : new TaskDetailViewModel(row.Task, Project, Shell);
    }

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
