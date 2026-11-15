using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Where the data lives.</summary>
    [ObservableProperty]
    private StorageBackend _backend;

    /// <summary>The address of the oloop server.</summary>
    [ObservableProperty]
    private string _serverUrl = "";

    /// <summary>The user name on the oloop server.</summary>
    [ObservableProperty]
    private string _serverUser = "";

    /// <summary>
    /// Whether this build can talk to a server. As long as it cannot, the
    /// dialog does not offer the choice. A test switches it on.
    /// </summary>
    public bool ServerBackendAvailable { get; init; }

    /// <summary>
    /// What the dialog may do with the open session: show where it stands, lock the keys, sign out.
    /// Null where there is no session (a test of the plain settings).
    /// </summary>
    public IServerSessionControl? ServerControl { get; init; }

    /// <summary>Whether the session can be looked at and controlled from here.</summary>
    public bool HasServerControl => ServerControl is not null;

    private ServerSessionState ServerState => ServerControl?.State ?? ServerSessionState.SignedOut;

    /// <summary>Where the session stands, in words.</summary>
    public string ServerStatus => ServerState switch
    {
        ServerSessionState.OpenKeyKept => "Signed in. The keys are kept on this computer, so the passphrase is not asked again.",
        ServerSessionState.Open => "Signed in. The keys are not kept: this computer has no safe place for them, so the passphrase is asked at every start.",
        ServerSessionState.Locked => "Signed in, keys locked. The passphrase is asked at the next load.",
        _ => "Not signed in on this computer.",
    };

    /// <summary>Whether there are keys to lock.</summary>
    public bool CanLockKeys => ServerState is ServerSessionState.Open or ServerSessionState.OpenKeyKept;

    /// <summary>Whether there is a sign-in to end.</summary>
    public bool CanSignOut => ServerState != ServerSessionState.SignedOut;

    /// <summary>Whether there is a session whose seen revisions can be forgotten.</summary>
    public bool CanForgetRevisions => ServerState != ServerSessionState.SignedOut;

    /// <summary>
    /// Forgets the highest revisions seen, for a server that was restored from a backup on purpose.
    /// </summary>
    [RelayCommand]
    private async Task ForgetRevisionsAsync()
    {
        if (ServerControl is null)
            return;

        await ServerControl.ForgetRevisionsAsync();
        RaiseServerState();
    }

    /// <summary>Forgets the keys on this computer; the sign-in stays.</summary>
    [RelayCommand]
    private async Task LockKeysAsync()
    {
        if (ServerControl is null)
            return;

        await ServerControl.LockAsync();
        RaiseServerState();
    }

    /// <summary>Cuts this computer off at the server and forgets everything kept here.</summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (ServerControl is null)
            return;

        await ServerControl.SignOutAsync();
        RaiseServerState();
    }

    private void RaiseServerState()
    {
        OnPropertyChanged(nameof(ServerStatus));
        OnPropertyChanged(nameof(CanLockKeys));
        OnPropertyChanged(nameof(CanSignOut));
        OnPropertyChanged(nameof(CanForgetRevisions));
    }

    /// <summary>Whether the data lives in a folder, so the folder is asked for.</summary>
    public bool IsFolderBackend => Backend == StorageBackend.Folder;

    /// <summary>Whether the data lives on a server, so the server is asked for.</summary>
    public bool IsServerBackend => Backend == StorageBackend.Server;

    /// <summary>The names of the backends, in the order of the combo box.</summary>
    public IReadOnlyList<string> BackendNames { get; } = ["Folder", "Server"];

    /// <summary>
    /// The backend as the index of the combo box; -1 while nothing is
    /// selected is ignored, like for the grouping.
    /// </summary>
    public int BackendIndex
    {
        get => (int)Backend;
        set
        {
            if (value < 0 || value >= BackendNames.Count)
                return;

            Backend = (StorageBackend)value;
        }
    }

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

    /// <summary>The size of the whole UI in percent.</summary>
    [ObservableProperty]
    private int _uiScalePercent = 100;

    /// <summary>
    /// The settings this view model edits. The values are copied out of it on
    /// construction and written back into it on <see cref="Save"/>.
    /// </summary>
    public SettingsViewModel(OloopSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Backend = settings.ResolveBackend();
        ServerUrl = settings.ServerUrl ?? "";
        ServerUser = settings.ServerUser ?? "";
        DataRoot = settings.ResolveDataRoot() ?? "";
        Actor = settings.ResolveActor();
        ShowClosed = settings.ShowClosed;
        Grouping = ParseGrouping(settings.Grouping);
        StaleDays = settings.StaleDays;
        UiScalePercent = Math.Clamp(settings.UiScalePercent,
            OloopSettings.MinUiScalePercent, OloopSettings.MaxUiScalePercent);
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
        => !string.IsNullOrWhiteSpace(Actor)
           && (IsServerBackend
               ? !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(ServerUser)
               : !string.IsNullOrWhiteSpace(DataRoot));

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

    /// <summary>The lower limit of the text size input.</summary>
    public decimal MinUiScalePercent => OloopSettings.MinUiScalePercent;

    /// <summary>The upper limit of the text size input.</summary>
    public decimal MaxUiScalePercent => OloopSettings.MaxUiScalePercent;

    /// <summary>
    /// The text size as a number for the numeric input. An empty input is
    /// ignored and keeps the current size, a value outside the range is
    /// pulled into it.
    /// </summary>
    public decimal? UiScaleValue
    {
        get => UiScalePercent;
        set
        {
            if (!value.HasValue)
                return;

            UiScalePercent = (int)Math.Clamp(Math.Round(value.Value),
                OloopSettings.MinUiScalePercent, OloopSettings.MaxUiScalePercent);
        }
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

        _settings.Backend = Backend == StorageBackend.Server ? "server" : "folder";
        _settings.DataRoot = DataRoot?.Trim();
        if (Backend == StorageBackend.Server)
        {
            _settings.ServerUrl = ServerEndpoint.Normalize(ServerUrl, out _);
            _settings.ServerUser = ServerUser.Trim();
        }
        _settings.Actor = Actor?.Trim() ?? "";
        _settings.ShowClosed = ShowClosed;
        _settings.Grouping = Grouping.ToString();
        _settings.StaleDays = StaleDays;
        _settings.UiScalePercent = UiScalePercent;

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

        if (IsServerBackend)
        {
            ServerEndpoint.Normalize(ServerUrl, out var urlError);
            if (urlError is not null)
            {
                Error = urlError;
                return;
            }

            Error = string.IsNullOrWhiteSpace(ServerUser) ? "The user name on the server is required." : null;
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

    partial void OnBackendChanged(StorageBackend value)
    {
        OnPropertyChanged(nameof(BackendIndex));
        OnPropertyChanged(nameof(IsFolderBackend));
        OnPropertyChanged(nameof(IsServerBackend));
        OnPropertyChanged(nameof(CanSave));
        UpdateValidation();
    }

    partial void OnServerUrlChanged(string value)
    {
        OnPropertyChanged(nameof(CanSave));
        UpdateValidation();
    }

    partial void OnServerUserChanged(string value)
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

    partial void OnUiScalePercentChanged(int value)
    {
        OnPropertyChanged(nameof(UiScaleValue));
    }

    partial void OnStaleDaysChanged(double? value)
    {
        OnPropertyChanged(nameof(StaleDaysValue));
    }
}
