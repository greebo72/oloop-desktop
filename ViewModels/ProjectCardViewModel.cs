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

    /// <summary>
    /// The repository of the project as one line, "host:path" like the CLI
    /// prints it. Null when the project has no reference path.
    /// </summary>
    public string? ReferenceText => string.IsNullOrWhiteSpace(Project.ReferencePath)
        ? null
        : string.IsNullOrWhiteSpace(Project.ReferenceHost)
            ? Project.ReferencePath
            : $"{Project.ReferenceHost}:{Project.ReferencePath}";

    /// <summary>Whether the project has a reference path.</summary>
    public bool HasReference => ReferenceText is not null;

    /// <summary>The share of closed tasks, 0..1 — the progress bar of the card.</summary>
    public double DoneRatio => Project.TaskCount == 0
        ? 0
        : (double)(Project.TaskCount - Project.OpenTaskCount) / Project.TaskCount;

    /// <summary>Whether the project has tasks, so a progress bar means something.</summary>
    public bool HasTasks => Project.TaskCount > 0;

    /// <summary>The open tasks per type, in the order of the types. Types without an open task are left out.</summary>
    public IReadOnlyList<TypeCountViewModel> OpenByType { get; private set; } = [];

    /// <summary>Whether the project has an open task at all.</summary>
    public bool HasOpenByType => OpenByType.Count > 0;

    /// <summary>How many open tasks have a due date that has passed.</summary>
    public int OverdueCount { get; private set; }

    /// <summary>Whether an open task of the project is overdue.</summary>
    public bool HasOverdue => OverdueCount > 0;

    /// <summary>"3 overdue".</summary>
    public string OverdueText => $"{OverdueCount} overdue";

    /// <summary>
    /// Counts the open tasks per type and the overdue ones. The tasks are
    /// those of this project; the dashboard hands them over when it builds
    /// the card, so no file is read again.
    /// </summary>
    public void ApplyTasks(IEnumerable<OloopTask> tasks, DateTime? today = null)
    {
        var day = (today ?? DateTime.Today).Date;
        var open = tasks.Where(t => !t.IsClosed).ToList();

        OpenByType = Enum.GetValues<TaskType>()
            .Select(type => new TypeCountViewModel(type, open.Count(t => t.Type == type)))
            .Where(c => c.Count > 0)
            .ToList();

        OverdueCount = open.Count(t => DueDate.IsOverdue(t, day));

        OnPropertyChanged(nameof(OpenByType));
        OnPropertyChanged(nameof(HasOpenByType));
        OnPropertyChanged(nameof(OverdueCount));
        OnPropertyChanged(nameof(HasOverdue));
        OnPropertyChanged(nameof(OverdueText));
    }
}
