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
    }

    /// <summary>The project this view belongs to.</summary>
    public OloopProject Project { get; }

    /// <summary>The shell, used to reach the shared data and commands.</summary>
    public MainViewModel Shell { get; }

    /// <summary>The project name.</summary>
    public string Name => Project.Name;

    /// <summary>Reloads the task data. The list and the detail are filled in step 3.</summary>
    public void Refresh()
    {
    }
}
