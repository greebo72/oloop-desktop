using System.Collections.ObjectModel;
using Avalonia.Controls;
using OloopApp.Controls;
using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The right pane of the split view: everything about one task — the type,
/// the status, the description and the comments.
/// </summary>
public partial class TaskDetailViewModel : ViewModelBase
{
    private Control? _descriptionView;

    /// <summary>Creates the detail of a task.</summary>
    public TaskDetailViewModel(OloopTask task, OloopProject project, MainViewModel shell)
    {
        Task = task;
        Project = project;
        Shell = shell;

        Title = task.Title;
        TypeName = task.Type.DisplayName();
        IsClosed = task.IsClosed;
        StatusLabel = task.IsClosed ? task.Type.ClosedLabel() : task.Type.OpenLabel();

        IconKey = task.Type switch
        {
            TaskType.todo => "IconChecklist",
            TaskType.question => "IconHelp",
            TaskType.decision => "IconGavel",
            TaskType.feature => "IconBuild",
            TaskType.bug => "IconBugReport",
            _ => "IconTaskAlt",
        };

        BackgroundKey = task.Type switch
        {
            TaskType.todo => "TypeTodoBgBrush",
            TaskType.question => "TypeQuestionBgBrush",
            TaskType.decision => "TypeDecisionBgBrush",
            TaskType.feature => "TypeFeatureBgBrush",
            TaskType.bug => "TypeBugBgBrush",
            _ => "TypeClosedBgBrush",
        };

        ForegroundKey = task.Type switch
        {
            TaskType.todo => "TypeTodoBrush",
            TaskType.question => "TypeQuestionBrush",
            TaskType.decision => "TypeDecisionBrush",
            TaskType.feature => "TypeFeatureBrush",
            TaskType.bug => "TypeBugBrush",
            _ => "TypeClosedBrush",
        };

        Labels = task.Labels;
        LabelsText = string.Join(", ", task.Labels);
        HasLabels = task.Labels.Count > 0;

        Description = task.Description ?? "";
        HasDescription = !string.IsNullOrWhiteSpace(Description);

        CreatedText = BuildStamp("Created", task.CreatedAt, task.CreatedBy);
        UpdatedText = BuildStamp("Updated", task.UpdatedAt, task.UpdatedBy);

        foreach (var comment in task.Comments)
            Comments.Add(new CommentViewModel(comment));

        HasComments = Comments.Count > 0;
    }

    /// <summary>The task behind the detail.</summary>
    public OloopTask Task { get; }

    /// <summary>The project the task belongs to.</summary>
    public OloopProject Project { get; }

    /// <summary>The shell of the app.</summary>
    public MainViewModel Shell { get; }

    /// <summary>The title of the task.</summary>
    public string Title { get; }

    /// <summary>The type of the task: "Todo", "Question", ...</summary>
    public string TypeName { get; }

    /// <summary>The status of the task: "Open" or the type specific closed label.</summary>
    public string StatusLabel { get; }

    /// <summary>Whether the task is closed.</summary>
    public bool IsClosed { get; }

    /// <summary>The icon of the task type, as a resource key of the theme.</summary>
    public string IconKey { get; }

    /// <summary>The background brush key of the type container.</summary>
    public string BackgroundKey { get; }

    /// <summary>The foreground brush key of the type container.</summary>
    public string ForegroundKey { get; }

    /// <summary>The labels of the task.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>The labels as one line, empty when the task has none.</summary>
    public string LabelsText { get; }

    /// <summary>Whether the task carries labels.</summary>
    public bool HasLabels { get; }

    /// <summary>The description of the task as raw Markdown.</summary>
    public string Description { get; }

    /// <summary>The description rendered as a control tree.</summary>
    public Control DescriptionView => _descriptionView ??= MarkdownRenderer.Render(Description);

    /// <summary>Whether the task has a description.</summary>
    public bool HasDescription { get; }

    /// <summary>"Created 2 h ago by timo".</summary>
    public string CreatedText { get; }

    /// <summary>"Updated 2 h ago by timo".</summary>
    public string UpdatedText { get; }

    /// <summary>The comments of the task.</summary>
    public ObservableCollection<CommentViewModel> Comments { get; } = [];

    /// <summary>Whether the task has comments.</summary>
    public bool HasComments { get; }

    private static string BuildStamp(string prefix, string timestamp, string actor)
    {
        var stamp = $"{prefix} {RelativeAge.Format(timestamp)} ago";
        return string.IsNullOrWhiteSpace(actor) ? stamp : $"{stamp} by {actor}";
    }
}
