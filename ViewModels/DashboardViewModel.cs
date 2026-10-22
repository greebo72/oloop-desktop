using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Filters;
using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>One choice of the due date filter box.</summary>
public sealed record DueFilterOption(DueFilter Filter, string Label);

/// <summary>One choice of the sort box of the task results.</summary>
public sealed record TaskSortOption(bool ByDueDate, string Label);

/// <summary>The project dashboard: one card per project.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Creates the dashboard. The task filters start with no filter.</summary>
    public DashboardViewModel()
    {
        _selectedDueFilter = DueFilterOptions[0];
        _selectedSort = SortOptions[0];
    }

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

    /// <summary>All tasks of the data root, before the search.</summary>
    private readonly List<OloopTask> _allTasks = [];

    /// <summary>The most tasks the result list shows. More would make the list slow.</summary>
    public const int MaxTaskResults = 200;

    /// <summary>
    /// What the search looks for: the project cards (false) or the tasks of
    /// all projects (true). Exactly one of them at a time. It starts as
    /// projects on every launch and is not stored.
    /// </summary>
    [ObservableProperty]
    private bool _searchTasks;

    /// <summary>Whether closed tasks are part of the task results. Off by default.</summary>
    [ObservableProperty]
    private bool _showClosed;

    /// <summary>The due date filter choices. The first one is the default, no filter.</summary>
    public IReadOnlyList<DueFilterOption> DueFilterOptions { get; } =
        Enum.GetValues<DueFilter>().Select(f => new DueFilterOption(f, f.Label())).ToList();

    /// <summary>The sort choices of the results: newest first, or by due date.</summary>
    public IReadOnlyList<TaskSortOption> SortOptions { get; } =
    [
        new TaskSortOption(false, "Recently updated"),
        new TaskSortOption(true, "Due date"),
    ];

    /// <summary>The due date filter. It combines with the search text and "Show closed".</summary>
    [ObservableProperty]
    private DueFilterOption _selectedDueFilter;

    /// <summary>How the task results are sorted.</summary>
    [ObservableProperty]
    private TaskSortOption _selectedSort;

    /// <summary>The tasks that match the search, newest first. Only filled in the task mode.</summary>
    public ObservableCollection<TaskRowViewModel> TaskResults { get; } = [];

    /// <summary>
    /// The command that opens a task found by the search. Set by the shell.
    /// </summary>
    public IRelayCommand<TaskRowViewModel>? OpenTaskCommand { get; set; }

    /// <summary>Whether the search looks for projects.</summary>
    public bool SearchProjects => !SearchTasks;

    /// <summary>The placeholder of the search field, it names what is searched.</summary>
    public string SearchPlaceholder => SearchTasks ? "Find tasks …" : "Find projects …";

    /// <summary>Whether the task search has no text yet. Then a hint is shown instead of a list.</summary>
    public bool ShowTaskHint => SearchTasks && !HasTaskQuery;

    /// <summary>
    /// Whether the task search has something to look for: a search text or a
    /// due date filter. A filter alone is enough, for example "Overdue".
    /// </summary>
    private bool HasTaskQuery
        => !string.IsNullOrWhiteSpace(SearchText) || SelectedDueFilter.Filter != DueFilter.All;

    /// <summary>Whether the task search has text but finds nothing.</summary>
    public bool HasNoTaskMatches => SearchTasks && !ShowTaskHint && TaskResults.Count == 0;

    /// <summary>Whether the result list of tasks is shown.</summary>
    public bool ShowTaskResults => SearchTasks && TaskResults.Count > 0;

    /// <summary>"Showing the first 200 of 340 tasks", empty when everything is shown.</summary>
    public string TaskResultsNote { get; private set; } = "";

    /// <summary>
    /// Whether the "New project" button of the header is shown: only in the
    /// project view, and only when there is a data folder to create it in.
    /// </summary>
    public bool ShowNewProjectButton => ShowProjects && !HasNoDataRoot;

    /// <summary>Whether the project part (cards, empty state) of the dashboard is shown.</summary>
    public bool ShowProjects => !SearchTasks;

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
    public bool ShowEmptyState => HasNoDataRoot || (!SearchTasks && (IsEmpty || HasNoMatches));

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
        OnPropertyChanged(nameof(ShowNewProjectButton));
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
        _allTasks.Clear();
        _allTasks.AddRange(snapshot.Tasks);

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

        ApplyTaskSearch();

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

    /// <summary>
    /// Fills <see cref="TaskResults"/>: the tasks of all projects whose title,
    /// description or labels contain the search text. Without text there is
    /// no list, only a hint.
    /// </summary>
    private void ApplyTaskSearch()
    {
        TaskResults.Clear();
        TaskResultsNote = "";

        var needle = SearchText?.Trim() ?? "";
        if (SearchTasks && HasTaskQuery)
        {
            var found = new TaskFilter
            {
                SearchText = needle,
                SearchDescription = true,
                ShowClosed = ShowClosed,
                Due = SelectedDueFilter.Filter,
            }
            .Apply(_allTasks)
            .OrderByDescending(t => t.UpdatedAt, StringComparer.Ordinal)
            .ToList();

            // Earliest due date first, tasks without a date at the end. The
            // sort is stable, so equal dates stay newest first.
            if (SelectedSort.ByDueDate)
            {
                found = found
                    .OrderBy(t => DueDate.Parse(t.DueDate) ?? DateTime.MaxValue)
                    .ToList();
            }

            foreach (var task in found.Take(MaxTaskResults))
                TaskResults.Add(new TaskRowViewModel(task));

            if (found.Count > MaxTaskResults)
                TaskResultsNote = $"Showing the first {MaxTaskResults} of {found.Count} tasks. Narrow the search to see the rest.";
        }

        OnPropertyChanged(nameof(TaskResultsNote));
        OnPropertyChanged(nameof(ShowTaskHint));
        OnPropertyChanged(nameof(HasNoTaskMatches));
        OnPropertyChanged(nameof(ShowTaskResults));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch();
        ApplyTaskSearch();
    }

    partial void OnShowClosedChanged(bool value)
        => ApplyTaskSearch();

    partial void OnSelectedDueFilterChanged(DueFilterOption value)
        => ApplyTaskSearch();

    partial void OnSelectedSortChanged(TaskSortOption value)
        => ApplyTaskSearch();

    partial void OnSearchTasksChanged(bool value)
    {
        OnPropertyChanged(nameof(SearchProjects));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(ShowProjects));
        OnPropertyChanged(nameof(ShowNewProjectButton));
        ApplyTaskSearch();
        RaiseEmptyStateChanged();
    }
}
