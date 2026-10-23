using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>One row of the task list.</summary>
public partial class TaskRowViewModel : ViewModelBase
{
    public TaskRowViewModel(OloopTask task, DateTimeOffset? now = null)
    {
        Task = task;
        Age = RelativeAge.Format(task.UpdatedAt, now);

        var today = (now ?? DateTimeOffset.Now).LocalDateTime.Date;
        DueDateText = Core.Grouping.DueDate.Display(task.DueDate);
        IsOverdue = Core.Grouping.DueDate.IsOverdue(task, today);
    }

    /// <summary>The task behind the row.</summary>
    public OloopTask Task { get; }

    /// <summary>Title of the task.</summary>
    public string Title => Task.Title;

    /// <summary>The project the task belongs to. The search over all projects shows it.</summary>
    public string ProjectName => Task.ProjectName;

    /// <summary>Type of the task, capitalized for the row.</summary>
    public string TypeName => Task.Type.DisplayName();

    /// <summary>How long ago the task was touched.</summary>
    public string Age { get; }

    /// <summary>The due date in the local format, null when there is none.</summary>
    public string? DueDateText { get; }

    /// <summary>Whether the task has a due date.</summary>
    public bool HasDueDate => DueDateText is not null;

    /// <summary>Whether the task is open and its due date has passed.</summary>
    public bool IsOverdue { get; }

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
