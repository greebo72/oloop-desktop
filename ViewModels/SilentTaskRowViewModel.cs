using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// One row of the "silent since" view.
///
/// It carries the project of the task, because the tasks come from all
/// projects — the name is what makes the row make sense outside of its
/// own project.
/// </summary>
public sealed class SilentTaskRowViewModel : ViewModelBase
{
    public SilentTaskRowViewModel(OloopTask task, OloopProject project, DateTimeOffset? now = null)
    {
        Task = task;
        Project = project;

        Age = RelativeAge.Format(task.UpdatedAt, now);

        // The days since the task was touched, as a number — the view is
        // about that number.
        var days = Silence.DaysSince(task.UpdatedAt, now ?? DateTimeOffset.UtcNow);
        Days = days;
    }

    /// <summary>The task behind the row.</summary>
    public OloopTask Task { get; }

    /// <summary>The project the task belongs to.</summary>
    public OloopProject Project { get; }

    /// <summary>Title of the task.</summary>
    public string Title => Task.Title;

    /// <summary>Type of the task, capitalized for the row.</summary>
    public string TypeName => Task.Type.DisplayName();

    /// <summary>How long ago the task was touched.</summary>
    public string Age { get; }

    /// <summary>The days since the task was touched.</summary>
    public int Days { get; }

    /// <summary>The name of the project the task belongs to.</summary>
    public string ProjectName => Project.Name;

    /// <summary>The labels as one line, empty when the task has none.</summary>
    public string LabelsText => string.Join(", ", Task.Labels);

    /// <summary>Whether the task carries labels.</summary>
    public bool HasLabels => Task.Labels.Count > 0;

    /// <summary>Whether the task is closed.</summary>
    public bool IsClosed => Task.IsClosed;

    /// <summary>The status label: "Open" or the type-specific closed label.</summary>
    public string StatusLabel => Task.IsClosed ? Task.Type.ClosedLabel() : Task.Type.OpenLabel();

    /// <summary>
    /// The icon of the task type, as a resource key of the theme.
    /// </summary>
    public string IconKey => Task.Type switch
    {
        TaskType.todo => "IconChecklist",
        TaskType.question => "IconHelp",
        TaskType.decision => "IconGavel",
        TaskType.feature => "IconBuild",
        TaskType.bug => "IconBugReport",
        _ => "IconTaskAlt",
    };

    /// <summary>
    /// The background brush key of the type container. A closed task is grey,
    /// whatever its type.
    /// </summary>
    public string BackgroundKey => Task.IsClosed ? "TypeClosedBgBrush" : Task.Type switch
    {
        TaskType.todo => "TypeTodoBgBrush",
        TaskType.question => "TypeQuestionBgBrush",
        TaskType.decision => "TypeDecisionBgBrush",
        TaskType.feature => "TypeFeatureBgBrush",
        TaskType.bug => "TypeBugBgBrush",
        _ => "TypeClosedBgBrush",
    };

    /// <summary>
    /// The foreground brush key of the type container. A closed task is grey,
    /// whatever its type.
    /// </summary>
    public string ForegroundKey => Task.IsClosed ? "TypeClosedBrush" : Task.Type switch
    {
        TaskType.todo => "TypeTodoBrush",
        TaskType.question => "TypeQuestionBrush",
        TaskType.decision => "TypeDecisionBrush",
        TaskType.feature => "TypeFeatureBrush",
        TaskType.bug => "TypeBugBrush",
        _ => "TypeClosedBrush",
    };
}
