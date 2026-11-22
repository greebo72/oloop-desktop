using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

public enum ImportStage
{
    /// <summary>No folder yet.</summary>
    Start,

    /// <summary>The plan is shown; nothing was written.</summary>
    Planned,

    /// <summary>Writing.</summary>
    Running,

    /// <summary>The report is shown.</summary>
    Done,
}

/// <summary>One project of the folder in the plan.</summary>
public sealed partial class ImportRowViewModel(ImportProjectPlan plan, Action changed) : ObservableObject
{
    public ImportProjectPlan Plan { get; } = plan;

    public string Name => Plan.Project.Name;
    public string Id => Plan.Project.Id;
    public string TasksText => Plan.TaskCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string ActionText => ImportReportText.ActionText(Plan);
    public string NewText => Plan.ToWrite.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string SkippedText => Plan.Skipped.Count == 0
        ? ""
        : string.Join(", ", Plan.Skipped.GroupBy(s => s.Reason)
            .Select(g => $"{g.Count()} {ImportReportText.ReasonText(g.Key)}"));

    /// <summary>A project without work cannot be chosen.</summary>
    public bool CanSelect => Plan.HasWork;

    public bool Selected
    {
        get => Plan.Selected;
        set
        {
            if (!CanSelect || Plan.Selected == value)
                return;

            Plan.Selected = value;
            OnPropertyChanged();
            changed();
        }
    }
}

/// <summary>
/// The dialog that imports a folder into the server (docs/SPEC-import-folder.md): choose the folder,
/// look at the plan, import, read the report. Nothing is written before the user presses Import.
/// </summary>
public partial class ImportFolderViewModel : ViewModelBase
{
    private readonly ServerOloopStore _store;
    private ImportPlan? _plan;
    private ImportReport? _report;
    private CancellationTokenSource? _cancel;

    public ImportFolderViewModel(ServerOloopStore store)
    {
        _store = store;
    }

    /// <summary>The window the view model runs in. Set before the first command.</summary>
    public IImportUi Ui { get; set; } = null!;

    public ObservableCollection<ImportRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStart), nameof(IsPlanned), nameof(IsRunning), nameof(IsDone), nameof(CanImport), nameof(CanClose))]
    private ImportStage _stage = ImportStage.Start;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _folder;

    [ObservableProperty]
    private string? _error;

    /// <summary>Files of the folder that could not be read, as one text; empty when all could.</summary>
    [ObservableProperty]
    private string _unreadableText = "";

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private double _progressDone;

    [ObservableProperty]
    private double _progressTotal = 1;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _reportText = "";

    public bool IsStart => Stage == ImportStage.Start;
    public bool IsPlanned => Stage == ImportStage.Planned;
    public bool IsRunning => Stage == ImportStage.Running;
    public bool IsDone => Stage == ImportStage.Done;

    public bool CanClose => Stage != ImportStage.Running;

    public bool CanImport => Stage == ImportStage.Planned && !IsBusy && (_plan?.TasksToWrite > 0 || _plan?.ProjectsToWrite > 0);

    /// <summary>Asks for a folder and makes the plan. Writes nothing.</summary>
    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var folder = await Ui.PickFolderAsync("Folder to import");
        if (folder is null)
            return;

        Error = null;
        IsBusy = true;
        try
        {
            _plan = await FolderImport.PlanAsync(folder, _store);
            Folder = folder;
            Rows.Clear();
            foreach (var project in _plan.Projects)
                Rows.Add(new ImportRowViewModel(project, Refresh));
            UnreadableText = string.Join("\n", _plan.Unreadable.Select(u => $"{u.Path}: {u.Reason}"));
            Stage = ImportStage.Planned;
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or OloopFormatException or OloopCryptoException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        if (_plan is null)
            return;

        Summary = _plan.TasksToWrite == 0 && _plan.ProjectsToWrite == 0
            ? "Nothing to import"
            : $"Import {Count(_plan.ProjectsToWrite, "project")}, {Count(_plan.TasksToWrite, "task")}";
        OnPropertyChanged(nameof(CanImport));
    }

    private static string Count(int n, string noun)
        => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Writes what the plan says. Cancelling stops after the current task.</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_plan is null || !CanImport)
            return;

        Error = null;
        IsBusy = true;
        Stage = ImportStage.Running;
        ProgressDone = 0;
        ProgressTotal = Math.Max(1, _plan.TasksToWrite);
        ProgressText = "Starting…";
        _cancel = new CancellationTokenSource();
        try
        {
            // Not Progress<T>: it posts to another thread, and the last report would come after the end.
            var progress = new InlineProgress(p =>
            {
                ProgressDone = p.Done;
                ProgressText = p.Project.Length == 0 ? "Done" : $"{p.Project}: {p.Done} of {p.Total} tasks";
            });
            _report = await FolderImport.RunAsync(_plan, _store, progress, _cancel.Token);
            ReportText = ImportReportText.Report(Folder ?? "", _plan, _report);
        }
        catch (Exception ex) when (ex is IOException or OloopFormatException or OloopCryptoException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            ReportText = _report is null ? "" : ImportReportText.Report(Folder ?? "", _plan, _report);
        }
        finally
        {
            _cancel.Dispose();
            _cancel = null;
            IsBusy = false;
            Stage = ImportStage.Done;
        }
    }

    /// <summary>Stops the import after the task that is being written. What was written stays.</summary>
    [RelayCommand]
    public void CancelRun() => _cancel?.Cancel();

    /// <summary>Back to the first page, to choose another folder.</summary>
    [RelayCommand]
    private void Restart()
    {
        _plan = null;
        _report = null;
        Rows.Clear();
        Folder = null;
        Error = null;
        Stage = ImportStage.Start;
    }

    /// <summary>Saves the plan or, after the import, the report as a text file.</summary>
    [RelayCommand]
    private async Task SaveTextAsync()
    {
        if (_plan is null)
            return;

        var path = await Ui.PickSaveFileAsync(
            IsDone ? "Save the report" : "Save the plan",
            IsDone ? "oloop-import-report.txt" : "oloop-import-plan.txt");
        if (path is null)
            return;

        var text = IsDone && _report is not null
            ? ImportReportText.Report(Folder ?? "", _plan, _report)
            : ImportReportText.Plan(Folder ?? "", _plan);
        try
        {
            await File.WriteAllTextAsync(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    private sealed class InlineProgress(Action<ImportProgress> handler) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => handler(value);
    }
}
