using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// One group of the task list: a header (when the tasks are grouped) and the
/// rows of the group. A group with a header can be collapsed; the rows of a
/// collapsed group are not built.
/// </summary>
public partial class TaskGroupViewModel : ViewModelBase
{
    private readonly IReadOnlyList<OloopTask> _tasks;
    private readonly TaskListViewModel? _owner;

    /// <summary>Creates a group of tasks.</summary>
    public TaskGroupViewModel(
        string? title,
        IEnumerable<OloopTask> tasks,
        TaskListViewModel? owner = null,
        bool expanded = true)
    {
        Title = title;
        _owner = owner;
        _tasks = tasks.ToList();

        // A group without a header cannot be collapsed: there would be
        // nothing to click to open it again.
        _isExpanded = expanded || !HasTitle;

        if (_isExpanded)
            BuildRows();

        ToggleCommand = new RelayCommand(Toggle);

        if (owner is not null)
        {
            SelectCommand = new RelayCommand<TaskRowViewModel>(owner.SelectRow);
            EditCommand = new AsyncRelayCommand<TaskRowViewModel>(owner.EditRowAsync);
        }
    }

    /// <summary>The header of the group. Null when the tasks are not grouped.</summary>
    public string? Title { get; }

    /// <summary>Whether the group has a header.</summary>
    public bool HasTitle => !string.IsNullOrEmpty(Title);

    /// <summary>The tasks of the group, also while the group is collapsed.</summary>
    public IReadOnlyList<OloopTask> Tasks => _tasks;

    /// <summary>How many tasks the group has, also while it is collapsed.</summary>
    public int Count => _tasks.Count;

    /// <summary>The header text with the count, for example "Overdue (3)".</summary>
    public string HeaderText => $"{Title} ({Count})";

    /// <summary>
    /// The rows of the group. Empty while the group is collapsed.
    /// </summary>
    public ObservableCollection<TaskRowViewModel> Rows { get; } = [];

    /// <summary>Whether the rows of the group are shown.</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Tooltip of the header: what a click does.</summary>
    public string ToggleToolTip => IsExpanded ? "Collapse the group" : "Expand the group";

    /// <summary>
    /// The name of the header for a screen reader, with the state of the
    /// group.
    /// </summary>
    public string AutomationName
        => $"{Title}, {Count} {(Count == 1 ? "task" : "tasks")}, {(IsExpanded ? "expanded" : "collapsed")}";

    /// <summary>Collapses or expands the group.</summary>
    public IRelayCommand ToggleCommand { get; }

    /// <summary>Shows the detail of a row. Null when the group stands alone.</summary>
    public IRelayCommand<TaskRowViewModel>? SelectCommand { get; }

    /// <summary>Opens the edit dialog of a row. Null when the group stands alone.</summary>
    public IAsyncRelayCommand<TaskRowViewModel>? EditCommand { get; }

    private void Toggle()
    {
        if (HasTitle)
            IsExpanded = !IsExpanded;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!HasTitle)
        {
            // Not collapsible: undo, the rows stay.
            if (!value)
                IsExpanded = true;
            return;
        }

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
            Rows.Add(new TaskRowViewModel(task));
    }
}
