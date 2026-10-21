using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>The project dashboard: one card per project.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Whether no data folder is set at all. The empty state then has to
    /// point at the settings instead of the project list.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoDataRoot;

    /// <summary>One card per project.</summary>
    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    /// <summary>Files that could not be read, with the reason.</summary>
    public ObservableCollection<UnreadableFile> Unreadable { get; } = [];

    /// <summary>"5 projects · 23 tasks".</summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// The command that opens a project. Set by the shell, so the cards can
    /// be clicked.
    /// </summary>
    public IRelayCommand<ProjectCardViewModel>? OpenProjectCommand { get; set; }

    /// <summary>
    /// The search text of the top bar. The project cards are filtered by it,
    /// so the placeholder of the search field ("Find projects …") is true.
    /// </summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>All projects of the data root, before the search.</summary>
    private readonly List<ProjectCardViewModel> _allProjects = [];

    /// <summary>
    /// How many files could not be read. The section is only shown when there
    /// is at least one.
    /// </summary>
    public int UnreadableCount => Unreadable.Count;

    /// <summary>Whether there is at least one file that could not be read.</summary>
    public bool HasUnreadable => Unreadable.Count > 0;

    /// <summary>
    /// The title of the empty state. Without a data folder the user has to
    /// pick one first, otherwise a project can be created.
    /// </summary>
    public string EmptyTitle => HasNoDataRoot
        ? "No data folder"
        : HasNoMatches
            ? "Nothing matches"
            : "No projects yet";

    /// <summary>The text of the empty state.</summary>
    public string EmptyText => HasNoDataRoot
        ? "Open the settings and choose the folder that holds your oloop projects."
        : HasNoMatches
            ? "No project matches the search. Clear the search to see them all."
            : "Create a project to get a folder in the data folder. Tasks live in that folder.";

    /// <summary>
    /// Whether the search hides every project. Then the list is empty, but
    /// the project list itself is not.
    /// </summary>
    public bool HasNoMatches => _allProjects.Count > 0 && Projects.Count == 0;

    /// <summary>
    /// Whether the empty state is shown: there is no data folder, or no
    /// project at all, or the search hides all of them.
    /// </summary>
    public bool ShowEmptyState => HasNoDataRoot || IsEmpty || HasNoMatches;

    /// <summary>
    /// The state of the empty card. Every part of it depends on a value that
    /// can change, so every change of them has to reach the view — otherwise
    /// the card keeps the state it had when the view was built.
    /// </summary>
    partial void OnHasNoDataRootChanged(bool value)
        => RaiseEmptyStateChanged();

    partial void OnIsEmptyChanged(bool value)
        => RaiseEmptyStateChanged();

    /// <summary>Raises every property the empty card is bound to.</summary>
    private void RaiseEmptyStateChanged()
    {
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>"1 file cannot be read" / "3 files cannot be read".</summary>
    public string UnreadableSummary
    {
        get
        {
            var count = Unreadable.Count;
            if (count == 0)
                return "";

            return count == 1
                ? "1 file cannot be read"
                : $"{count} files cannot be read";
        }
    }

    public void Update(OloopSnapshot snapshot)
    {
        _allProjects.Clear();
        foreach (var project in snapshot.Projects)
            _allProjects.Add(new ProjectCardViewModel
            {
                Project = project,
                OpenCommand = OpenProjectCommand,
            });

        ApplySearch();

        Unreadable.Clear();
        foreach (var file in snapshot.Unreadable)
            Unreadable.Add(file);

        OnPropertyChanged(nameof(UnreadableCount));
        OnPropertyChanged(nameof(HasUnreadable));
        OnPropertyChanged(nameof(UnreadableSummary));

        var tasks = snapshot.Tasks.Count;
        var projects = snapshot.Projects.Count;
        Summary = projects == 0
            ? ""
            : $"{projects} project{(projects == 1 ? "" : "s")} · {tasks} task{(tasks == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Shows the projects whose name matches the search. An empty search shows
    /// all of them.
    /// </summary>
    private void ApplySearch()
    {
        var needle = SearchText?.Trim() ?? "";

        Projects.Clear();
        foreach (var project in _allProjects)
        {
            if (needle.Length > 0
                && !project.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Projects.Add(project);
        }

        // The empty state is only shown when there is really nothing — not
        // when the search hides every project.
        IsEmpty = _allProjects.Count == 0;

        RaiseEmptyStateChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch();
    }
}
