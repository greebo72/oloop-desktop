using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// One group of the task list: a header (when the tasks are grouped) and the
/// rows of the group.
/// </summary>
public partial class TaskGroupViewModel : ViewModelBase
{
    /// <summary>Creates a group of tasks.</summary>
    public TaskGroupViewModel(string? title, IEnumerable<OloopTask> tasks, TaskListViewModel? owner = null)
    {
        Title = title;

        foreach (var task in tasks)
            Rows.Add(new TaskRowViewModel(task));

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

    /// <summary>The rows of the group.</summary>
    public ObservableCollection<TaskRowViewModel> Rows { get; } = [];

    /// <summary>Shows the detail of a row. Null when the group stands alone.</summary>
    public IRelayCommand<TaskRowViewModel>? SelectCommand { get; }

    /// <summary>Opens the edit dialog of a row. Null when the group stands alone.</summary>
    public IAsyncRelayCommand<TaskRowViewModel>? EditCommand { get; }
}
