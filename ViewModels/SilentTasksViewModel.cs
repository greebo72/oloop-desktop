using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Filters;
using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The "silent since" view: the tasks of all projects that have not been
/// touched for a long time, the oldest first.
///
/// It is the view the spec calls a first-class view (SPEC.md, Native
/// clients): the point of oloop is to see what has been quiet after a week
/// or two, and that question spans all projects.
///
/// The view shows the same rows as the task list, but every row carries the
/// project it belongs to — the tasks come from everywhere.
/// </summary>
public partial class SilentTasksViewModel : ViewModelBase
{
    /// <summary>
    /// The days a task has to be quiet before it shows up. Zero means no
    /// lower bound — every task that is not closed is listed.
    /// </summary>
    private const double NoMinimumAge = 0;

    private int _openCount;
    private int _totalCount;
    private bool _isEmpty;

    /// <summary>Creates the view.</summary>
    public SilentTasksViewModel(MainViewModel shell)
    {
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));

        shell.PropertyChanged += OnShellPropertyChanged;

        Refresh();
    }

    /// <summary>The shell, used for the search text and the task data.</summary>
    public MainViewModel Shell { get; }

    /// <summary>The groups of the list, one per silence bucket.</summary>
    public ObservableCollection<SilenceGroupViewModel> Groups { get; } = [];

    /// <summary>The task shown in the detail pane.</summary>
    [ObservableProperty]
    private TaskDetailViewModel? _selected;

    /// <summary>
    /// The days a task has to be quiet before it shows up. Zero lists every
    /// open task.
    /// </summary>
    [ObservableProperty]
    private double _minimumAgeDays = NoMinimumAge;

    /// <summary>Whether closed tasks are part of the list.</summary>
    [ObservableProperty]
    private bool _showClosed;

    /// <summary>
    /// The open tasks that pass the filter.
    /// </summary>
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

    /// <summary>All tasks before the filter.</summary>
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

    /// <summary>
    /// The minimum age as a number for the numeric input. An empty input
    /// means no lower bound, which is a zero here.
    /// </summary>
    public decimal? MinimumAgeValue
    {
        get => MinimumAgeDays <= 0 ? null : (decimal)MinimumAgeDays;
        set => MinimumAgeDays = value.HasValue ? (double)value.Value : NoMinimumAge;
    }

    /// <summary>
    /// The title of the empty state. It says what the user can do about it.
    /// </summary>
    public string EmptyTitle => "Nothing is silent";

    /// <summary>The text of the empty state.</summary>
    public string EmptyText => ShowClosed
        ? "No task of any project has been quiet for that long."
        : "No open task of any project has been quiet for that long.";

    /// <summary>
    /// The header of the view: "12 tasks · 3 projects" — how much is quiet
    /// and where it lives.
    /// </summary>
    public string Summary
    {
        get
        {
            var tasks = Groups.Sum(g => g.Rows.Count);
            var projects = Groups
                .SelectMany(g => g.Rows)
                .Select(r => r.ProjectName)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (tasks == 0)
                return "";

            return projects == 1
                ? $"{tasks} task{(tasks == 1 ? "" : "s")} in 1 project"
                : $"{tasks} task{(tasks == 1 ? "" : "s")} in {projects} projects";
        }
    }

    /// <summary>
    /// The lower bound of the age, as the text of the header: "quiet for at
    /// least 14 days" or "everything that is open".
    /// </summary>
    public string FilterText => MinimumAgeDays <= 0
        ? "Every open task, the quietest first"
        : $"Quiet for at least {MinimumAgeDays:0.##} days, the quietest first";

    /// <summary>Re-applies the filter and the buckets.</summary>
    public void Refresh()
    {
        var all = Shell.Tasks.Where(t => ShowClosed || !t.IsClosed).ToList();
        TotalCount = all.Count;

        var filter = new TaskFilter
        {
            SearchText = Shell.SearchText,
            ShowClosed = true,
        };

        var filtered = filter.Apply(all).ToList();

        // The lower bound of the age. The filter of the task list only
        // knows a filter that hides the young tasks — here the bound is
        // the other way round: the old ones are the ones wanted.
        if (MinimumAgeDays > 0)
        {
            var now = DateTimeOffset.UtcNow;
            filtered = filtered
                .Where(t => Silence.DaysSince(t.UpdatedAt, now) >= MinimumAgeDays)
                .ToList();
        }

        OpenCount = filtered.Count(t => !t.IsClosed);
        IsEmpty = filtered.Count == 0;

        // The detail of the selected task is rebuilt from the fresh data,
        // so it never shows the version from before the last write — except
        // for an edit that is under way.
        if (Selected is { } selected)
        {
            var fresh = filtered.FirstOrDefault(t => t.Id == selected.Task.Id);
            Selected = fresh is null
                ? null
                : selected.Task.SameContentAs(fresh)
                    ? selected
                    : TaskDetailViewModel.Open(fresh, ProjectOf(fresh), Shell);
        }

        Groups.Clear();
        foreach (var group in Silence.Group(filtered))
        {
            Groups.Add(new SilenceGroupViewModel(group.Title, group.Tasks, this));
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(FilterText));
    }

    /// <summary>Shows the detail of the given row.</summary>
    public void SelectRow(SilentTaskRowViewModel? row)
    {
        Selected = row is null
            ? null
            : TaskDetailViewModel.Open(row.Task, row.Project, Shell);
    }

    /// <summary>
    /// Puts the given row into edit mode: the row is selected and its detail
    /// edits in place.
    /// </summary>
    public async Task EditRowAsync(SilentTaskRowViewModel? row)
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

    /// <summary>
    /// The project a task belongs to. The task knows its project id, the
    /// shell knows the projects.
    /// </summary>
    public OloopProject ProjectOf(OloopTask task)
        => Shell.Projects.FirstOrDefault(p => p.Id == task.ProjectId)
           ?? new OloopProject
           {
               FormatVersion = 1,
               Id = task.ProjectId,
               Name = task.ProjectName,
               CreatedAt = task.CreatedAt,
           };

    partial void OnMinimumAgeDaysChanged(double value)
    {
        OnPropertyChanged(nameof(MinimumAgeValue));
        Refresh();
    }

    partial void OnShowClosedChanged(bool value)
    {
        Refresh();
    }

    partial void OnSelectedChanged(TaskDetailViewModel? value)
        => OnPropertyChanged(nameof(HasSelection));

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SearchText))
            Refresh();
    }
}
