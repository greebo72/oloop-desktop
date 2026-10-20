using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Io;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The shell of the app: the top bar, the navigation between the dashboard
/// and a project, the settings and the search.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly OloopSettings _settings = OloopSettings.Load();

    /// <summary>The dashboard with the project cards.</summary>
    public DashboardViewModel Dashboard { get; } = new();

    /// <summary>The task list and detail of the open project. Null on the dashboard.</summary>
    [ObservableProperty]
    private ProjectSplitViewModel? _split;

    /// <summary>The data root the app reads from.</summary>
    [ObservableProperty]
    private string _dataRoot = "";

    /// <summary>The actor written into createdBy/updatedBy.</summary>
    [ObservableProperty]
    private string _actor = "";

    /// <summary>The search text of the top bar.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>A short status line under the top bar.</summary>
    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>Whether the app is loading.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>All tasks of the loaded data root, kept for the split view.</summary>
    public ObservableCollection<OloopTask> Tasks { get; } = [];

    /// <summary>All projects of the loaded data root.</summary>
    public ObservableCollection<OloopProject> Projects { get; } = [];

    /// <summary>Whether the dashboard is shown (no project open).</summary>
    public bool IsDashboard => Split is null;

    public MainViewModel()
    {
        _dataRoot = _settings.ResolveDataRoot() ?? "";
        _actor = _settings.ResolveActor();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenProjectCommand = new RelayCommand<ProjectCardViewModel>(OpenProject);
        BackToDashboardCommand = new RelayCommand(BackToDashboard);
    }

    /// <summary>Reloads the data root.</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Opens a project in the split view.</summary>
    public RelayCommand<ProjectCardViewModel> OpenProjectCommand { get; }

    /// <summary>Returns to the dashboard.</summary>
    public RelayCommand BackToDashboardCommand { get; }

    /// <summary>
    /// Loads the data root. A missing root is not an error — the app shows an
    /// empty state and the settings dialog.
    /// </summary>
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var root = DataRoot?.Trim() ?? "";
            if (root.Length == 0)
            {
                StatusMessage = "No data folder set. Open the settings to choose one.";
                Dashboard.Update(OloopSnapshot.Empty);
                Projects.Clear();
                Tasks.Clear();
                return;
            }

            if (!Directory.Exists(root))
            {
                StatusMessage = $"The data folder '{root}' does not exist.";
                Dashboard.Update(OloopSnapshot.Empty);
                Projects.Clear();
                Tasks.Clear();
                return;
            }

            var store = new OloopStore(root);
            var snapshot = await store.LoadAsync();

            Projects.Clear();
            foreach (var project in snapshot.Projects)
                Projects.Add(project);

            Tasks.Clear();
            foreach (var task in snapshot.Tasks)
                Tasks.Add(task);

            Dashboard.Update(snapshot);

            StatusMessage = snapshot.Unreadable.Count == 0
                ? ""
                : $"{snapshot.Unreadable.Count} file(s) could not be read.";

            Split?.Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not read the data folder: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsDashboard));
        }
    }

    private void OpenProject(ProjectCardViewModel? card)
    {
        if (card is null)
            return;

        Split = new ProjectSplitViewModel(card.Project, this);
        OnPropertyChanged(nameof(IsDashboard));
    }

    private void BackToDashboard()
    {
        Split = null;
        OnPropertyChanged(nameof(IsDashboard));
    }

    /// <summary>Saves the settings file.</summary>
    public void SaveSettings()
    {
        _settings.DataRoot = DataRoot?.Trim();
        _settings.Actor = Actor?.Trim() ?? "";
        _settings.Save();
    }
}
