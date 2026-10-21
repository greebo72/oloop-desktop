using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Grouping;
using OloopApp.Core.Io;

namespace OloopApp.ViewModels;

/// <summary>
/// The settings dialog: the data root, the actor and the preferences of the
/// task list. It wraps an <see cref="OloopSettings"/> instance and writes the
/// edited values back into it when the user saves.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly OloopSettings _settings;
    private string? _error;

    /// <summary>The data root the app reads the projects from.</summary>
    [ObservableProperty]
    private string _dataRoot = "";

    /// <summary>The name written into createdBy/updatedBy of every task.</summary>
    [ObservableProperty]
    private string _actor = "";

    /// <summary>
    /// Whether the name is missing. Then the field is empty and the app
    /// asks for it before the first write.
    /// </summary>
    public bool HasNoActor => string.IsNullOrWhiteSpace(Actor);

    /// <summary>Whether closed tasks are shown in the list.</summary>
    [ObservableProperty]
    private bool _showClosed;

    /// <summary>How the task list is grouped.</summary>
    [ObservableProperty]
    private GroupingMode _grouping;

    /// <summary>The "silent since" filter in days. Null switches it off.</summary>
    [ObservableProperty]
    private double? _staleDays;

    /// <summary>
    /// The settings this view model edits. The values are copied out of it on
    /// construction and written back into it on <see cref="Save"/>.
    /// </summary>
    public SettingsViewModel(OloopSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        DataRoot = settings.ResolveDataRoot() ?? "";
        Actor = settings.ResolveActor();
        ShowClosed = settings.ShowClosed;
        Grouping = ParseGrouping(settings.Grouping);
        StaleDays = settings.StaleDays;
    }

    /// <summary>
    /// The message shown when the settings cannot be used: the actor is empty
    /// or the data root is missing or is no directory. Null when everything is
    /// fine.
    /// </summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>
    /// Whether the dialog can be saved. The actor is required because it goes
    /// into the createdBy/updatedBy of every written task, the data root
    /// because there is nothing to show without it.
    /// </summary>
    public bool CanSave
        => !string.IsNullOrWhiteSpace(Actor) && !string.IsNullOrWhiteSpace(DataRoot);

    /// <summary>All grouping modes, in the order of the combo box.</summary>
    public IReadOnlyList<GroupingMode> GroupingModes { get; } = Enum.GetValues<GroupingMode>();

    /// <summary>
    /// The grouping as the index of the combo box. The index is used instead
    /// of the value itself, because a combo box reports -1 as long as nothing
    /// is selected and that must not overwrite the current grouping.
    /// </summary>
    public int GroupingIndex
    {
        get => (int)Grouping;
        set
        {
            if (value < 0 || value >= GroupingModes.Count)
                return;

            Grouping = GroupingModes[value];
        }
    }

    /// <summary>
    /// The stale days as a number for the numeric input. An empty input means
    /// that the filter is off, which is a null value here.
    /// </summary>
    public decimal? StaleDaysValue
    {
        get => StaleDays.HasValue ? (decimal)StaleDays.Value : null;
        set => StaleDays = value.HasValue ? (double)value.Value : null;
    }

    /// <summary>
    /// Writes the settings file. A path that cannot be written is reported
    /// through <see cref="Error"/> instead of an exception.
    /// </summary>
    public void Save()
    {
        SaveTo(_settings.FilePath);
    }

    /// <summary>
    /// Writes the settings file to the given path. Mainly used by the tests.
    /// </summary>
    public void SaveTo(string path)
    {
        UpdateValidation();
        if (Error is not null)
            return;

        _settings.DataRoot = DataRoot?.Trim();
        _settings.Actor = Actor?.Trim() ?? "";
        _settings.ShowClosed = ShowClosed;
        _settings.Grouping = Grouping.ToString();
        _settings.StaleDays = StaleDays;

        try
        {
            _settings.Save(path);

            // From now on the settings go to the same file.
            _settings.FilePath = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            Error = $"The settings could not be written: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads the grouping name of the settings. An unknown name falls back to
    /// <see cref="GroupingMode.None"/>.
    /// </summary>
    private static GroupingMode ParseGrouping(string? name)
        => Enum.TryParse<GroupingMode>(name, ignoreCase: true, out var mode)
            ? mode
            : GroupingMode.None;

    /// <summary>
    /// Recomputes the error message. An empty actor, a missing data root and
    /// a data root that is no directory are all reported.
    /// </summary>
    private void UpdateValidation()
    {
        if (string.IsNullOrWhiteSpace(Actor))
        {
            Error = "The actor name is required. It is written into every task you create or change.";
            return;
        }

        var root = DataRoot?.Trim() ?? "";
        if (root.Length == 0)
        {
            Error = "The data folder is required.";
            return;
        }

        if (!Directory.Exists(root))
        {
            Error = $"The data folder '{root}' does not exist.";
            return;
        }

        Error = null;
    }

    partial void OnDataRootChanged(string value)
    {
        OnPropertyChanged(nameof(CanSave));
        UpdateValidation();
    }

    partial void OnActorChanged(string value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasNoActor));
        UpdateValidation();
    }

    partial void OnGroupingChanged(GroupingMode value)
    {
        OnPropertyChanged(nameof(GroupingIndex));
    }

    partial void OnStaleDaysChanged(double? value)
    {
        OnPropertyChanged(nameof(StaleDaysValue));
    }
}
