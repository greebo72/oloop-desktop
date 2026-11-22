using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

/// <summary>
/// A project in the dialog that gives an agent more projects: the box, and the role the agent has in it
/// already. A project the agent has cannot be ticked again (SPEC-agent-grant-projects.md).
/// </summary>
public partial class GrantProjectChoice(OloopProject project) : ObservableObject
{
    public Guid Id { get; } = Guid.Parse(project.Id);

    public string Name { get; } = project.Name;

    /// <summary>The role of the agent in this project, or null when it is not a member.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect), nameof(RoleText))]
    private string? _existingRole;

    public bool CanSelect => ExistingRole is null;

    public string RoleText => ExistingRole is null ? "" : $"has it ({ExistingRole})";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Gives an existing agent more projects (SPEC-agent-grant-projects.md). The credentials file of the
/// agent does not change. The key chain of the agent is checked before anything is handed over; when
/// it does not check out the dialog says so and offers nothing more.
/// </summary>
public partial class GrantProjectsViewModel : ViewModelBase
{
    private readonly AgentService _service;
    private readonly AgentInfo _agent;

    public GrantProjectsViewModel(
        AgentService service, AgentInfo agent, IEnumerable<OloopProject> projects, Func<DateTimeOffset>? now = null)
    {
        _service = service;
        _agent = agent;
        AgentName = agent.Name;
        TokenExpired = agent.StatusAt((now ?? (() => DateTimeOffset.UtcNow))()) == "expired";
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var choice = new GrantProjectChoice(project);
            choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanGrant));
            Projects.Add(choice);
        }
    }

    public string AgentName { get; }

    /// <summary>The token has ended: the projects can still be handed over, but the agent needs a new token to use them.</summary>
    public bool TokenExpired { get; }

    public ObservableCollection<GrantProjectChoice> Projects { get; } = [];

    /// <summary>Gives the projects with the role read instead of write.</summary>
    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGrant))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGrant), nameof(NothingToGrant))]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _error;

    /// <summary>The key chain of the agent did not check out. Nothing is offered then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGrant))]
    private bool _chainInvalid;

    /// <summary>What was handed over, shown once the user has granted something.</summary>
    [ObservableProperty]
    private string? _result;

    public bool CanGrant => IsLoaded && !IsBusy && !ChainInvalid && Projects.Any(p => p.CanSelect && p.IsSelected);

    /// <summary>The agent has every project of the user already.</summary>
    public bool NothingToGrant => IsLoaded && Projects.All(p => !p.CanSelect);

    /// <summary>Reads which projects the agent has already. A server that cannot be reached is shown as the error.</summary>
    public async Task LoadAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            var roles = await _service.RolesOfAsync(_agent.Id, Projects.Select(p => p.Id));
            foreach (var project in Projects)
                project.ExistingRole = roles.GetValueOrDefault(project.Id);
            IsLoaded = true;
            OnPropertyChanged(nameof(NothingToGrant));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Hands over the ticked projects. Projects that went through are marked as the agent's; the ones
    /// that did not stay ticked, so one more click tries them again.
    /// </summary>
    public async Task GrantAsync()
    {
        if (!CanGrant)
            return;

        Error = null;
        Result = null;
        IsBusy = true;
        try
        {
            var chosen = Projects.Where(p => p.CanSelect && p.IsSelected).ToList();
            var answer = await _service.GrantProjectsAsync(_agent, [.. chosen.Select(p => p.Id)], ReadOnly);

            var role = ReadOnly ? "read" : "write";
            foreach (var project in chosen.Where(p => answer.Granted.Contains(p.Id)))
            {
                project.IsSelected = false;
                project.ExistingRole = role;
            }

            OnPropertyChanged(nameof(NothingToGrant));
            if (answer.Granted.Count > 0)
            {
                Result = $"{answer.Granted.Count} project{(answer.Granted.Count == 1 ? "" : "s")} handed over to {AgentName}."
                         + (TokenExpired ? " The token of this agent has ended: it needs a new token to use them." : "");
            }

            if (answer.Failed.Count > 0)
            {
                var names = Projects.ToDictionary(p => p.Id, p => p.Name);
                Error = "Not handed over: "
                        + string.Join("; ", answer.Failed.Select(f => $"{names.GetValueOrDefault(f.ProjectId, f.ProjectId.ToString())} ({f.Reason})"))
                        + ". They are still ticked: try again.";
            }
        }
        catch (OloopCryptoException ex)
        {
            ChainInvalid = true;
            Error = $"The keys of this agent do not check out ({ex.Message}). Nothing was handed over. "
                    + "Revoke the agent and make a new one.";
        }
        catch (Exception ex) when (ex is IOException or OloopFormatException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
