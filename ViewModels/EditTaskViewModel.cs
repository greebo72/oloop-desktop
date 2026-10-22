using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// Collects the input of the edit task dialog: title, description and labels
/// of an existing task.
///
/// The type of a task is immutable in the oloop format, so it is shown but
/// not editable. Like the new task dialog this view model only gathers the
/// values — the shell performs the write.
/// </summary>
public partial class EditTaskViewModel : ViewModelBase
{
    private readonly string _originalTitle;
    private readonly string _originalDescription;
    private readonly IReadOnlyList<string> _originalLabels;
    private readonly DateTime? _originalDueDate;

    /// <summary>The task behind the dialog.</summary>
    public OloopTask Task { get; }

    /// <summary>
    /// Creates the view model for an existing task, pre-filled with its values.
    /// </summary>
    /// <param name="task">The task to edit.</param>
    public EditTaskViewModel(OloopTask task)
    {
        Task = task;

        _originalTitle = task.Title;
        _originalDescription = task.Description ?? "";
        _originalLabels = task.Labels;
        _originalDueDate = Core.Grouping.DueDate.Parse(task.DueDate);
        _dueDate = _originalDueDate;

        _title = task.Title;
        _description = task.Description ?? "";
        _labelsText = string.Join(", ", task.Labels);
    }

    /// <summary>The title of the task. Required, 1 to 300 characters.</summary>
    [ObservableProperty]
    private string _title;

    /// <summary>The description of the task, as Markdown.</summary>
    [ObservableProperty]
    private string _description;

    /// <summary>The labels of the task as comma separated free text.</summary>
    [ObservableProperty]
    private string _labelsText;

    /// <summary>
    /// The due date picked in the calendar, null for none. Only the date part
    /// counts, a time or a time zone is never stored.
    /// </summary>
    [ObservableProperty]
    private DateTime? _dueDate;

    /// <summary>The due date as it is written to the file, null for none.</summary>
    public string? DueDateStorage => Core.Grouping.DueDate.ToStorage(DueDate);

    /// <summary>Removes the due date.</summary>
    [RelayCommand]
    private void ClearDueDate() => DueDate = null;

    /// <summary>
    /// Whether the input is complete enough to be written: the title must not
    /// be empty and must fit into the format.
    /// </summary>
    public bool CanSave => TitleValidation.IsValid(Title);

    /// <summary>
    /// Whether something changed compared to the task as it was loaded.
    /// </summary>
    public bool IsDirty =>
        !string.Equals(TrimmedTitle, _originalTitle.Trim(), StringComparison.Ordinal) ||
        !string.Equals(TrimmedDescription, _originalDescription.Trim('\n'), StringComparison.Ordinal) ||
        !Labels.SequenceEqual(_originalLabels, StringComparer.Ordinal) ||
        DueDate?.Date != _originalDueDate?.Date;

    /// <summary>The type of the task — immutable, shown but not editable.</summary>
    public TaskType Type => Task.Type;

    /// <summary>The name of the type of the task ("Todo", "Bug", …).</summary>
    public string TypeName => Task.Type.DisplayName();

    /// <summary>The theme key of the icon of the type.</summary>
    public string IconKey => Task.Type switch
    {
        TaskType.todo => "IconChecklist",
        TaskType.question => "IconHelp",
        TaskType.decision => "IconGavel",
        TaskType.feature => "IconBuild",
        TaskType.bug => "IconBugReport",
        _ => "IconTaskAlt",
    };

    /// <summary>The theme key of the container brush of the type.</summary>
    public string BackgroundKey => Task.Type switch
    {
        TaskType.todo => "TypeTodoBgBrush",
        TaskType.question => "TypeQuestionBgBrush",
        TaskType.decision => "TypeDecisionBgBrush",
        TaskType.feature => "TypeFeatureBgBrush",
        TaskType.bug => "TypeBugBgBrush",
        _ => "TypeClosedBgBrush",
    };

    /// <summary>The theme key of the foreground brush of the type.</summary>
    public string ForegroundKey => Task.Type switch
    {
        TaskType.todo => "TypeTodoBrush",
        TaskType.question => "TypeQuestionBrush",
        TaskType.decision => "TypeDecisionBrush",
        TaskType.feature => "TypeFeatureBrush",
        TaskType.bug => "TypeBugBrush",
        _ => "TypeClosedBrush",
    };

    /// <summary>The trimmed title — what the store would write.</summary>
    public string TrimmedTitle => (Title ?? "").Trim();

    /// <summary>The description — what the store would write.</summary>
    public string TrimmedDescription => (Description ?? "").Trim();

    /// <summary>
    /// The labels of the task, normalized the way the store normalizes them.
    /// </summary>
    public IReadOnlyList<string> Labels => LabelParser.Parse(LabelsText);

    /// <summary>The validation message for the title, empty when the title is fine.</summary>
    public string? TitleError => TitleValidation.Error(Title);

    partial void OnTitleChanged(string value) => NotifyState();

    partial void OnDescriptionChanged(string value) => NotifyState();

    partial void OnLabelsTextChanged(string value) => NotifyState();

    partial void OnDueDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(DueDateStorage));
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(TrimmedTitle));
        OnPropertyChanged(nameof(TrimmedDescription));
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(TitleError));
    }
}
