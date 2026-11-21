using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

/// <summary>A project in the list of the new agent, with the box that says whether the agent gets it.</summary>
public partial class ProjectChoice(OloopProject project) : ObservableObject
{
    public Guid Id { get; } = Guid.Parse(project.Id);

    public string Name { get; } = project.Name;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>An entry of the "valid for" and "renewable until" lists.</summary>
public sealed record AgentPeriod(string Label, int Days);

/// <summary>
/// Makes an agent (AGENTS.md, "Creating an agent"; SPEC-agent-management.md): asks for the name, the
/// projects, the role and the times, makes the agent on the server and writes its credentials file.
/// The dialog has two states: the form, and the result that names the file.
/// </summary>
public partial class NewAgentViewModel : ViewModelBase
{
    /// <summary>The file ends with the days: a year is 365 days, the server counts the same.</summary>
    public static IReadOnlyList<AgentPeriod> ValidityChoices { get; } =
    [
        new("30 days", 30), new("90 days", 90), new("1 year", 365), new("2 years", 730),
    ];

    /// <summary>Zero days is "not renewable": the renewal period ends with the token.</summary>
    public static IReadOnlyList<AgentPeriod> RenewalChoices { get; } =
    [
        new("Not renewable", 0), new("1 year", 365), new("2 years", 730),
    ];

    private readonly AgentService _service;
    private readonly IAgentUi _ui;
    private readonly Func<DateTimeOffset> _now;

    public NewAgentViewModel(
        AgentService service, IEnumerable<OloopProject> projects, IAgentUi ui, Func<DateTimeOffset>? now = null)
    {
        _service = service;
        _ui = ui;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var choice = new ProjectChoice(project);
            choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanCreate));
            Projects.Add(choice);
        }
    }

    public ObservableCollection<ProjectChoice> Projects { get; } = [];

    public IReadOnlyList<AgentPeriod> Validities => ValidityChoices;

    public IReadOnlyList<AgentPeriod> Renewals => RenewalChoices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string _name = "";

    /// <summary>Gives the agent the role read instead of write.</summary>
    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    private int _validityIndex = 1;

    [ObservableProperty]
    private int _renewalIndex = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    /// <summary>True once the agent is made: the dialog then shows <see cref="FilePath"/> and the command.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForm))]
    private bool _isDone;

    public bool IsForm => !IsDone;

    public string? FilePath { get; private set; }

    public string? ExpiresText { get; private set; }

    public string? UseCommand => FilePath is null ? null : $"export OLOOP_AGENT_CREDENTIALS={ShellQuote(FilePath)}";

    public bool CanCreate
        => !IsBusy && NameIsValid(Name) && Projects.Any(p => p.IsSelected);

    /// <summary>The name as the server will store it: trimmed, 1 to 100 characters.</summary>
    public static bool NameIsValid(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 0 && trimmed.EnumerateRunes().Count() <= AgentService.NameLimit;
    }

    /// <summary>
    /// Asks where to save, makes the agent and writes the file. Returns true when the agent exists and
    /// its file is written; the dialog then shows the result. A cancelled file chooser is not an error:
    /// nothing is made.
    /// </summary>
    public async Task<bool> CreateAsync()
    {
        if (!CanCreate)
            return false;

        Error = null;
        var path = await _ui.PickSaveFileAsync("Save the credentials file of the agent", SuggestFileName(Name));
        if (path is null)
            return false;

        IsBusy = true;
        AgentCredentials? credentials = null;
        try
        {
            var now = _now();
            var expires = now.AddDays(ValidityChoices[ValidityIndex].Days);
            var renewDays = RenewalChoices[RenewalIndex].Days;
            var request = new NewAgent(
                Name, [.. Projects.Where(p => p.IsSelected).Select(p => p.Id)], ReadOnly,
                expires, renewDays == 0 ? expires : now.AddDays(renewDays));

            credentials = await _service.CreateAsync(request);
            AgentCredentialsFile.Write(path, credentials);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OloopFormatException)
        {
            Error = await AfterFailureAsync(ex, credentials);
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        FilePath = path;
        ExpiresText = credentials.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd");
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(ExpiresText));
        OnPropertyChanged(nameof(UseCommand));
        IsDone = true;
        return true;
    }

    /// <summary>
    /// The message for the user. When the agent was made but its file could not be written, the agent
    /// is revoked again: nobody holds its secret key, so it could never be used.
    /// </summary>
    private async Task<string> AfterFailureAsync(Exception ex, AgentCredentials? made)
    {
        if (ex is AgentCreateException { RolledBack: false } create)
            return $"{create.Message} Revoke the agent \"{Name.Trim()}\" in the list of agents.";
        if (made is null)
            return ex.Message;

        try
        {
            await _service.RevokeAsync(made.AgentId);
            return $"The file could not be written ({ex.Message}). The agent was revoked again; try another place.";
        }
        catch (IOException)
        {
            return $"The file could not be written ({ex.Message}) and the agent could not be revoked again. Revoke the agent \"{Name.Trim()}\" in the list of agents.";
        }
    }

    /// <summary>A file name from the agent name: letters, digits, dash and underscore; the rest becomes a dash.</summary>
    public static string SuggestFileName(string name)
    {
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var stem = new string(chars).Trim('-');
        return (stem.Length == 0 ? "agent" : stem) + ".json";
    }

    private static string ShellQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";
}
