using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>The project dashboard: one card per project.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>One card per project.</summary>
    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    /// <summary>Files that could not be read, with the reason.</summary>
    public ObservableCollection<UnreadableFile> Unreadable { get; } = [];

    /// <summary>"5 projects · 23 tasks".</summary>
    public string Summary { get; set; } = "";

    public void Update(OloopSnapshot snapshot)
    {
        Projects.Clear();
        foreach (var project in snapshot.Projects)
            Projects.Add(new ProjectCardViewModel { Project = project });

        Unreadable.Clear();
        foreach (var file in snapshot.Unreadable)
            Unreadable.Add(file);

        IsEmpty = snapshot.Projects.Count == 0;

        var tasks = snapshot.Tasks.Count;
        var projects = snapshot.Projects.Count;
        Summary = projects == 0
            ? ""
            : $"{projects} project{(projects == 1 ? "" : "s")} · {tasks} task{(tasks == 1 ? "" : "s")}";
    }
}
