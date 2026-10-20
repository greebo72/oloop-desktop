using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Io;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The "new project" dialog: a name and an optional reference path. The
/// project folder and the project file are written by the store.
/// </summary>
public partial class NewProjectViewModel : ViewModelBase
{
    private readonly OloopStore _store;
    private string? _error;

    /// <summary>The name of the project. Required, at most 200 characters.</summary>
    [ObservableProperty]
    private string _name = "";

    /// <summary>
    /// The path of the repository the project belongs to. Optional — the
    /// project is then only known by its name.
    /// </summary>
    [ObservableProperty]
    private string _referencePath = "";

    /// <summary>
    /// The message shown when the project cannot be created: the name is
    /// empty or too long, or the store refused the name.
    /// </summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>
    /// Whether the dialog can be saved: the name must have between 1 and 200
    /// characters.
    /// </summary>
    public bool CanSave
    {
        get
        {
            var trimmed = Name?.Trim() ?? "";
            return trimmed.Length is >= 1 and <= 200;
        }
    }

    /// <summary>
    /// The host the project is created on. It is written into the project
    /// file, so the reference path can be resolved on the right machine.
    /// </summary>
    public string ReferenceHost => Environment.MachineName;

    /// <summary>The actor who creates the project.</summary>
    public string Actor { get; }

    /// <summary>
    /// Creates the view model for a store and the actor who creates the
    /// project.
    /// </summary>
    public NewProjectViewModel(OloopStore store, string actor)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Actor = actor ?? "";
    }

    /// <summary>
    /// Creates the project. Returns the new project or null when the store
    /// refused it — the reason is in <see cref="Error"/>.
    /// </summary>
    public async Task<OloopProject?> SaveAsync(CancellationToken ct = default)
    {
        var trimmed = Name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            Error = "The name of the project is required.";
            return null;
        }

        if (trimmed.EnumerateRunes().Count() > 200)
        {
            Error = "The name of the project must be at most 200 characters.";
            return null;
        }

        var referencePath = string.IsNullOrWhiteSpace(ReferencePath)
            ? null
            : ReferencePath.Trim();

        try
        {
            var project = await _store
                .CreateProjectAsync(trimmed, referencePath, ReferenceHost, ct)
                .ConfigureAwait(true);

            Error = null;
            return project;
        }
        catch (OloopFormatException ex)
        {
            Error = ex.Message;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"The project could not be created: {ex.Message}";
            return null;
        }
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSave));
    }
}
