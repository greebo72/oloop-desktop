using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The split view of one project: the task list on the left, the detail of
/// the selected task on the right.
/// </summary>
public partial class ProjectSplitViewModel : ViewModelBase
{
    public ProjectSplitViewModel(OloopProject project, MainViewModel shell)
    {
        Project = project;
        Shell = shell;
        TaskList = new TaskListViewModel(project, shell);

        TaskList.PropertyChanged += OnTaskListPropertyChanged;
    }

    /// <summary>The project this view belongs to.</summary>
    public OloopProject Project { get; }

    /// <summary>The shell, used to reach the shared data and commands.</summary>
    public MainViewModel Shell { get; }

    /// <summary>The project name.</summary>
    public string Name => Project.Name;

    /// <summary>The task list of the left pane.</summary>
    public TaskListViewModel TaskList { get; }

    /// <summary>Reloads the task data of the list.</summary>
    public void Refresh()
    {
        TaskList.Refresh();
    }

    /// <summary>Whether a task is shown in the detail pane.</summary>
    public bool HasSelection => TaskList.HasSelection;

    private void OnTaskListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskListViewModel.HasSelection))
            OnPropertyChanged(nameof(HasSelection));
    }
}
