using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Controls;
using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The right pane of the split view: everything about one task — the type,
/// the status, the description and the comments.
///
/// The pane edits in place: title, description and labels turn into input
/// fields with an explicit Save. It never writes on its own: every change
/// goes through the shell, which owns the write path and asks the user what
/// to do when the file on disk has changed.
///
/// Input that is not written yet (an edit that changed something, a typed
/// comment) is registered with the shell as a draft, so it is not lost when
/// the user selects another task or leaves the project.
/// </summary>
public partial class TaskDetailViewModel : ViewModelBase
{
    private Control? _descriptionView;
    private Bitmap? _image;
    private string? _imageError;
    private bool _imageLoaded;
    private string? _imagePath;
    private OloopProject? _imageProject;
    private bool _isWriting;
    private bool _retired;

    /// <summary>
    /// Opens the detail of a task. When the task has a draft — an edit that
    /// changed something, or a typed comment — the draft is picked up
    /// again, so the input survives a reload and a change of the selection.
    ///
    /// An edit is kept as it is, with the version of the task it started
    /// from, so saving it still notices a change on disk. A typed comment
    /// moves over to the detail of the fresh task.
    /// </summary>
    public static TaskDetailViewModel Open(OloopTask task, OloopProject project, MainViewModel shell)
    {
        var draft = shell.TryGetDraft(task.Id);

        if (draft is { IsWriting: false, Editor.IsDirty: true })
            return draft;

        var detail = new TaskDetailViewModel(task, project, shell);
        if (draft is not null)
            detail.CommentText = draft.CommentText;

        return detail;
    }

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

        // The image is read on the first access, not here.
        _imagePath = task.ImagePath;
        _imageProject = project;

        CreatedText = BuildStamp("Created", task.CreatedAt, task.CreatedBy);
        UpdatedText = BuildStamp("Updated", task.UpdatedAt, task.UpdatedBy);

        foreach (var comment in task.Comments)
            Comments.Add(new CommentViewModel(comment));

        HasComments = Comments.Count > 0;

        EditCommand = new RelayCommand(BeginEdit);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(CancelEdit);
        CloseCommand = new AsyncRelayCommand(() => SetClosedAsync(true), () => !IsEditing);
        ReopenCommand = new AsyncRelayCommand(() => SetClosedAsync(false), () => !IsEditing);
        AddCommentCommand = new AsyncRelayCommand(AddCommentAsync, () => CanAddComment);
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

    /// <summary>
    /// The image of the task, when it has one and the file is there. Null
    /// otherwise — a task without an image shows nothing at all.
    ///
    /// The file is read on the first access, not in the constructor: reading
    /// a bitmap needs a running platform, and the view model is built before
    /// the view asks for it.
    /// </summary>
    public Bitmap? Image
    {
        get
        {
            EnsureImageLoaded();
            return _image;
        }
    }

    /// <summary>Whether the task has an image that can be shown.</summary>
    public bool HasImage
    {
        get
        {
            EnsureImageLoaded();
            return _image is not null;
        }
    }

    /// <summary>
    /// The message shown when the task points at an image that is not there.
    /// Null when there is no image at all.
    /// </summary>
    public string? ImageError
    {
        get
        {
            EnsureImageLoaded();
            return _imageError;
        }
    }

    /// <summary>Whether the image could not be loaded.</summary>
    public bool HasImageError => ImageError is not null;

    /// <summary>"Created 2 h ago by alice".</summary>
    public string CreatedText { get; }

    /// <summary>"Updated 2 h ago by alice".</summary>
    public string UpdatedText { get; }

    /// <summary>The comments of the task.</summary>
    public ObservableCollection<CommentViewModel> Comments { get; } = [];

    /// <summary>Whether the task has comments.</summary>
    public bool HasComments { get; }

    /// <summary>The text of the comment field.</summary>
    [ObservableProperty]
    private string _commentText = "";

    /// <summary>Whether the comment field has text that can be written.</summary>
    public bool CanAddComment => !IsEditing && !string.IsNullOrWhiteSpace(CommentText);

    /// <summary>
    /// The input of the edit mode. Null while the detail only shows the task.
    /// </summary>
    [ObservableProperty]
    private EditTaskViewModel? _editor;

    /// <summary>Whether title, description and labels are input fields.</summary>
    public bool IsEditing => Editor is not null;

    /// <summary>Whether the Close button is shown: an open task that is not being edited.</summary>
    public bool ShowClose => !IsClosed && !IsEditing;

    /// <summary>Whether the Reopen button is shown: a closed task that is not being edited.</summary>
    public bool ShowReopen => IsClosed && !IsEditing;

    /// <summary>Whether the edit can be saved: the title is valid.</summary>
    public bool CanSave => Editor?.CanSave ?? false;

    /// <summary>
    /// Whether the pane holds input that is not written yet. The shell asks
    /// before it closes the window.
    /// </summary>
    public bool HasUnsavedChanges
        => (Editor?.IsDirty ?? false) || !string.IsNullOrWhiteSpace(CommentText);

    /// <summary>True while a save or a comment is being written.</summary>
    internal bool IsWriting => _isWriting;

    /// <summary>Switches title, description and labels to input fields.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Writes the edit.</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>Leaves the edit mode and drops what was typed.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Closes the task.</summary>
    public IAsyncRelayCommand CloseCommand { get; }

    /// <summary>Reopens the task.</summary>
    public IAsyncRelayCommand ReopenCommand { get; }

    /// <summary>Appends the comment of the comment field to the task.</summary>
    public IAsyncRelayCommand AddCommentCommand { get; }

    partial void OnCommentTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddComment));
        AddCommentCommand.NotifyCanExecuteChanged();
        UpdateDraft();
    }

    partial void OnEditorChanging(EditTaskViewModel? value)
    {
        if (Editor is { } old)
            old.PropertyChanged -= OnEditorPropertyChanged;
    }

    partial void OnEditorChanged(EditTaskViewModel? value)
    {
        if (value is not null)
            value.PropertyChanged += OnEditorPropertyChanged;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowClose));
        OnPropertyChanged(nameof(ShowReopen));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanAddComment));
        CloseCommand.NotifyCanExecuteChanged();
        ReopenCommand.NotifyCanExecuteChanged();
        AddCommentCommand.NotifyCanExecuteChanged();
        UpdateDraft();
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanSave));
        UpdateDraft();
    }

    /// <summary>
    /// Registers the detail with the shell while it holds input that is not
    /// written, and removes it again once there is nothing left to lose.
    /// </summary>
    private void UpdateDraft()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));

        // A detail that was replaced by a newer one of the same task must not
        // register again — its input has moved on or is gone.
        if (_retired)
            return;

        if (HasUnsavedChanges)
            Shell.SetDraft(Task.Id, this);
        else
            Shell.ClearDraft(Task.Id, this);
    }

    /// <summary>
    /// Switches to the edit mode. The input fields start with the values of
    /// the task, so nothing counts as changed until the user types.
    /// </summary>
    public void BeginEdit()
    {
        if (IsEditing)
            return;

        Editor = new EditTaskViewModel(Task);
    }

    /// <summary>Leaves the edit mode. What was typed in it is dropped.</summary>
    public void CancelEdit() => Editor = null;

    private async Task SaveAsync()
    {
        var editor = Editor;
        if (editor is null || !editor.CanSave)
            return;

        // Nothing changed — there is nothing to write.
        if (!editor.IsDirty)
        {
            CancelEdit();
            return;
        }

        // The shell reloads the data root after the write, and the list
        // builds the detail of the task anew. While the write runs, this
        // detail must not be picked up as the draft again.
        _isWriting = true;
        try
        {
            var outcome = await Shell.SaveTaskEditAsync(
                Task, Project, editor.TrimmedTitle, editor.TrimmedDescription, editor.Labels);

            // Saved: the list shows the new detail. Discarded: the user chose
            // to reload. Either way the edit of this detail is over.
            if (outcome != WriteOutcome.NotSaved)
            {
                _retired = true;
                Shell.ClearDraft(Task.Id, this);
                Editor = null;
            }
        }
        finally
        {
            _isWriting = false;
        }
    }

    private async Task SetClosedAsync(bool closed)
    {
        // The shell reloads the data root after the write, and the list
        // rebuilds the detail of the selected task from the fresh data. A
        // comment that was typed moves over to the new detail.
        await Shell.SetTaskClosedAsync(Task, Project, closed);
    }

    private async Task AddCommentAsync()
    {
        var body = CommentText?.Trim() ?? "";
        if (body.Length == 0)
            return;

        // The field is emptied before the write: the shell reloads and the
        // list builds the detail anew, and a typed comment would move over to
        // it. If the write fails, the text comes back.
        CommentText = "";
        _isWriting = true;
        try
        {
            if (!await Shell.AddCommentAsync(Task, Project, body))
                CommentText = body;
        }
        finally
        {
            _isWriting = false;
        }
    }

    private static string BuildStamp(string prefix, string timestamp, string actor)
    {
        var stamp = $"{prefix} {RelativeAge.Format(timestamp)} ago";
        return string.IsNullOrWhiteSpace(actor) ? stamp : $"{stamp} by {actor}";
    }

    /// <summary>
    /// Loads the image of the task. A path that points nowhere, or a file that
    /// is not an image, is reported instead of shown as a broken picture.
    ///
    /// The file is read only once — the result is kept.
    /// </summary>
    private void EnsureImageLoaded()
    {
        if (_imageLoaded)
            return;

        _imageLoaded = true;

        if (string.IsNullOrWhiteSpace(_imagePath) || _imageProject is null)
            return;

        var store = Shell.StoreOrNull;
        var path = store?.ResolveImagePath(_imageProject, _imagePath);

        if (path is null)
        {
            _imageError = "The image path of the task is not valid.";
            return;
        }

        if (!File.Exists(path))
        {
            _imageError = "The image of the task is missing.";
            return;
        }

        try
        {
            _image = new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            _imageError = "The image of the task could not be read.";
        }
    }
}
