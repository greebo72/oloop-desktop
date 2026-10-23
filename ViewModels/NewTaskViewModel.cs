using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Filters;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// Collects the input of the new task dialog: title, description, labels and
/// the type of the task.
///
/// The view model only gathers the values — writing them to disk is the job
/// of the shell, which calls <see cref="OloopApp.Core.Io.OloopStore"/>
/// after the dialog was confirmed.
/// </summary>
public partial class NewTaskViewModel : ViewModelBase
{
    /// <summary>The maximum length of a task title (the format allows 300 characters).</summary>
    public const int MaxTitleLength = 300;

    /// <summary>The project the new task belongs to.</summary>
    public OloopProject Project { get; }

    /// <summary>The type options of the type row.</summary>
    public IReadOnlyList<TaskTypeOption> TypeOptions { get; }

    /// <summary>
    /// Creates the view model for a new task in the given project.
    /// </summary>
    /// <param name="project">The project the task will belong to.</param>
    /// <param name="preferredType">
    /// The type the dialog starts with — the type the user picked last. Null
    /// means the first type of the row.
    /// </param>
    /// <param name="knownLabels">
    /// The labels that already exist, for the suggestions of the label input.
    /// </param>
    public NewTaskViewModel(
        OloopProject project,
        TaskType? preferredType = null,
        IReadOnlyList<LabelEntry>? knownLabels = null)
    {
        Project = project;
        KnownLabels = knownLabels ?? [];
        TypeOptions = TaskTypeExtensions.All
            .Select(type => new TaskTypeOption(type, SelectType))
            .ToList();

        // Setting the type also updates the selection of the type row. The
        // type is set to the same value it already has, so the change
        // handler does not run — the selection is set here.
        Type = preferredType ?? TaskType.todo;
        UpdateSelection(Type);
    }

    /// <summary>The title of the task. Required, 1 to 300 characters.</summary>
    [ObservableProperty]
    private string _title = "";

    /// <summary>The description of the task, as Markdown.</summary>
    [ObservableProperty]
    private string _description = "";

    /// <summary>The labels of the task as comma separated free text.</summary>
    [ObservableProperty]
    private string _labelsText = "";

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

    /// <summary>The type of the task.</summary>
    [ObservableProperty]
    private TaskType _type = TaskType.todo;

    /// <summary>The five task types, in the order of the type row.</summary>
    public IReadOnlyList<TaskType> Types => TaskTypeExtensions.All;

    /// <summary>
    /// Whether the input is complete enough to be written: the title must not
    /// be empty and must fit into the format.
    /// </summary>
    public bool CanSave => TitleValidation.IsValid(Title);

    /// <summary>The validation message for the title, empty when the title is fine.</summary>
    public string? TitleError => TitleValidation.Error(Title);

    /// <summary>The trimmed title — what the store would write.</summary>
    public string TrimmedTitle => (Title ?? "").Trim();

    /// <summary>The description — what the store would write.</summary>
    public string TrimmedDescription => (Description ?? "").Trim();

    /// <summary>The labels that already exist, offered while the user types.</summary>
    public IReadOnlyList<LabelEntry> KnownLabels { get; }

    /// <summary>
    /// The labels of the task, normalized the way the store normalizes them.
    /// A label that only differs in case from an existing one takes the
    /// spelling of the existing one.
    /// </summary>
    public IReadOnlyList<string> Labels
        => LabelSuggester.Adopt(LabelParser.Parse(LabelsText), keep: null, KnownLabels).Labels;

    /// <summary>The labels that were written in the spelling of an existing label.</summary>
    public IReadOnlyList<AdoptedLabel> AdoptedLabels
        => LabelSuggester.Adopt(LabelParser.Parse(LabelsText), keep: null, KnownLabels).Adopted;

    partial void OnTitleChanged(string value) => NotifySaveState();

    partial void OnDescriptionChanged(string value) => NotifySaveState();

    partial void OnLabelsTextChanged(string value) => NotifySaveState();

    partial void OnDueDateChanged(DateTime? value) => OnPropertyChanged(nameof(DueDateStorage));

    partial void OnTypeChanged(TaskType value)
    {
        UpdateSelection(value);
        OnPropertyChanged(nameof(TypeName));
    }

    /// <summary>The name of the selected type ("Todo", "Bug", …).</summary>
    public string TypeName => Type.DisplayName();

    /// <summary>
    /// Picks a type — called by the type row of the dialog.
    /// </summary>
    /// <param name="type">The type to pick.</param>
    public void SelectType(TaskType type) => Type = type;

    private void UpdateSelection(TaskType type)
    {
        foreach (var option in TypeOptions)
            option.IsSelected = option.Type == type;
    }

    private void NotifySaveState()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(TrimmedTitle));
        OnPropertyChanged(nameof(TrimmedDescription));
        OnPropertyChanged(nameof(Labels));
        OnPropertyChanged(nameof(TitleError));
    }
}
