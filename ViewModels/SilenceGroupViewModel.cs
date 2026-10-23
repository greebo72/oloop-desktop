using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// One bucket of the "silent since" view: a header and the rows of the
/// bucket. The bucket can be collapsed; the rows of a collapsed bucket are
/// not built.
/// </summary>
public partial class SilenceGroupViewModel : ViewModelBase
{
    private readonly IReadOnlyList<OloopTask> _tasks;
    private readonly SilentTasksViewModel? _owner;

    /// <summary>Creates a bucket of the view.</summary>
    public SilenceGroupViewModel(
        string title,
        IEnumerable<OloopTask> tasks,
        SilentTasksViewModel? owner = null,
        bool expanded = true)
    {
        Title = title;
        _owner = owner;
        _tasks = tasks.ToList();
        _isExpanded = expanded;

        if (_isExpanded)
            BuildRows();

        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);

        if (owner is not null)
        {
            SelectCommand = new RelayCommand<SilentTaskRowViewModel>(owner.SelectRow);
            EditCommand = new AsyncRelayCommand<SilentTaskRowViewModel>(owner.EditRowAsync);
        }
    }

    /// <summary>The header of the bucket.</summary>
    public string Title { get; }

    /// <summary>The tasks of the bucket, also while it is collapsed.</summary>
    public IReadOnlyList<OloopTask> Tasks => _tasks;

    /// <summary>How many tasks the bucket has, also while it is collapsed.</summary>
    public int Count => _tasks.Count;

    /// <summary>The header text with the count, for example "Over 30 days (3)".</summary>
    public string HeaderText => $"{Title} ({Count})";

    /// <summary>The rows of the bucket. Empty while the bucket is collapsed.</summary>
    public ObservableCollection<SilentTaskRowViewModel> Rows { get; } = [];

    /// <summary>Whether the rows of the bucket are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Tooltip of the header: what a click does.</summary>
    public string ToggleToolTip => IsExpanded ? "Collapse the group" : "Expand the group";

    /// <summary>The name of the header for a screen reader, with the state.</summary>
    public string AutomationName
        => $"{Title}, {Count} {(Count == 1 ? "task" : "tasks")}, {(IsExpanded ? "expanded" : "collapsed")}";

    /// <summary>Collapses or expands the bucket.</summary>
    public IRelayCommand ToggleCommand { get; }

    /// <summary>Shows the detail of a row. Null when the group stands alone.</summary>
    public IRelayCommand<SilentTaskRowViewModel>? SelectCommand { get; }

    /// <summary>Opens the edit dialog of a row. Null when the group stands alone.</summary>
    public IAsyncRelayCommand<SilentTaskRowViewModel>? EditCommand { get; }

    partial void OnIsExpandedChanged(bool value)
    {
        Rows.Clear();
        if (value)
            BuildRows();

        OnPropertyChanged(nameof(ToggleToolTip));
        OnPropertyChanged(nameof(AutomationName));

        _owner?.GroupToggled(this);
    }

    private void BuildRows()
    {
        foreach (var task in _tasks)
        {
            var project = _owner?.ProjectOf(task) ?? FallbackProject(task);
            Rows.Add(new SilentTaskRowViewModel(task, project));
        }
    }

    /// <summary>
    /// The project of a task, when the owner does not know it. Only used
    /// when the group stands alone — then the name of the task is all that
    /// is left.
    /// </summary>
    private static OloopProject FallbackProject(OloopTask task) => new()
    {
        FormatVersion = 1,
        Id = task.ProjectId,
        Name = task.ProjectName,
        CreatedAt = task.CreatedAt,
    };
}
