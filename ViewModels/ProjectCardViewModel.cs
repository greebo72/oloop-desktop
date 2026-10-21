using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>One project card on the dashboard.</summary>
public partial class ProjectCardViewModel : ViewModelBase
{
    public required OloopProject Project { get; init; }

    /// <summary>Opens the project in the split view.</summary>
    public IRelayCommand? OpenCommand { get; init; }

    /// <summary>The project name.</summary>
    public string Name => Project.Name;

    /// <summary>"4 open of 7".</summary>
    public string OpenOfTotal => $"{Project.OpenTaskCount} open of {Project.TaskCount}";

    /// <summary>The share of open tasks, 0..1.</summary>
    public double OpenRatio => Project.TaskCount == 0 ? 0 : (double)Project.OpenTaskCount / Project.TaskCount;

    /// <summary>"Updated 4 min ago" — empty when the project has no tasks yet.</summary>
    public string UpdatedText => Project.LatestUpdatedAt is null
        ? "no tasks yet"
        : $"Updated {RelativeAge.Format(Project.LatestUpdatedAt)} ago";

    /// <summary>The machine name the reference path belongs to.</summary>
    public string? ReferenceHost => Project.ReferenceHost;
}
