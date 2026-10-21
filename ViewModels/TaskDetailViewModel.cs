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
/// The pane is read-only in the sense that it never writes on its own: every
/// change goes through the shell, which owns the write path and asks the
/// user what to do when the file on disk has changed.
/// </summary>
public partial class TaskDetailViewModel : ViewModelBase
{
    private Control? _descriptionView;
    private Bitmap? _image;
    private string? _imageError;
    private bool _imageLoaded;
    private string? _imagePath;
    private OloopProject? _imageProject;

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

        EditCommand = new AsyncRelayCommand(EditAsync);
        CloseCommand = new AsyncRelayCommand(() => SetClosedAsync(true));
        ReopenCommand = new AsyncRelayCommand(() => SetClosedAsync(false));
        AddCommentCommand = new AsyncRelayCommand(AddCommentAsync);
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
    public bool CanAddComment => !string.IsNullOrWhiteSpace(CommentText);

    /// <summary>Opens the edit dialog for this task.</summary>
    public IAsyncRelayCommand EditCommand { get; }

    /// <summary>Closes the task.</summary>
    public IAsyncRelayCommand CloseCommand { get; }

    /// <summary>Reopens the task.</summary>
    public IAsyncRelayCommand ReopenCommand { get; }

    /// <summary>Appends the comment of the comment field to the task.</summary>
    public IAsyncRelayCommand AddCommentCommand { get; }

    partial void OnCommentTextChanged(string value)
        => OnPropertyChanged(nameof(CanAddComment));

    private async Task EditAsync()
        => await Shell.OpenEditTaskAsync(Task, Project);

    private async Task SetClosedAsync(bool closed)
    {
        // The shell reloads the data root after the write, and the list
        // rebuilds the detail of the selected task from the fresh data.
        await Shell.SetTaskClosedAsync(Task, Project, closed);
    }

    private async Task AddCommentAsync()
    {
        var body = CommentText?.Trim() ?? "";
        if (body.Length == 0)
            return;

        if (!await Shell.AddCommentAsync(Task, Project, body))
            return;

        CommentText = "";
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
