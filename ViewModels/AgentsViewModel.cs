using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

/// <summary>One agent in the list.</summary>
public sealed class AgentRowViewModel(AgentInfo info, DateTimeOffset now)
{
    public AgentInfo Info { get; } = info;

    public string Name => Info.Name;

    public string Status => Info.StatusAt(now);

    public bool IsRevoked => Info.Revoked;

    public string ExpiresText => Date(Info.ExpiresAt);

    public string RenewText => Date(Info.RenewUntil);

    public string LastUsedText => Info.LastUsedAt is null ? "never" : Date(Info.LastUsedAt);

    /// <summary>A revoked agent answers 409 to a new token.</summary>
    public bool CanIssueToken => !Info.Revoked;

    public bool CanRevoke => !Info.Revoked;

    private static string Date(DateTimeOffset? moment)
        => moment is null ? "–" : moment.Value.ToLocalTime().ToString("yyyy-MM-dd");
}

/// <summary>
/// The agents of the signed-in user: the list, creating one, a new token and revoking
/// (SPEC-agent-management.md). Revoked agents stay in the list, below the others, because an agent is
/// never deleted.
/// </summary>
public partial class AgentsViewModel : ViewModelBase
{
    private readonly AgentService _service;
    private readonly Func<IReadOnlyList<OloopProject>> _projects;
    private readonly Func<DateTimeOffset> _now;

    private IAgentUi _ui = null!;

    public AgentsViewModel(
        AgentService service, Func<IReadOnlyList<OloopProject>> projects, Func<DateTimeOffset>? now = null)
    {
        _service = service;
        _projects = projects;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The window the view model runs in. Set before the first command.</summary>
    public IAgentUi Ui
    {
        get => _ui;
        set => _ui = value;
    }

    public ObservableCollection<AgentRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    /// <summary>A message that is not an error, for example after a new token.</summary>
    [ObservableProperty]
    private string? _notice;

    public bool IsEmpty => IsLoaded && Rows.Count == 0;

    /// <summary>Reads the list. A server that cannot be reached is shown as the error, not thrown.</summary>
    [RelayCommand]
    public Task LoadAsync()
    {
        Error = null;
        return RefreshRowsAsync();
    }

    /// <summary>
    /// Reads the list again after a command, and leaves the message of that command alone: the error of
    /// a refused token must still be there when the list is shown.
    /// </summary>
    private async Task RefreshRowsAsync()
    {
        IsBusy = true;
        try
        {
            var now = _now();
            var agents = await _service.ListAsync();
            Rows.Clear();
            foreach (var agent in agents.OrderBy(a => a.Revoked).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
                Rows.Add(new AgentRowViewModel(agent, now));
            IsLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
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

    [RelayCommand]
    private async Task NewAgentAsync()
    {
        Notice = null;
        var dialog = new NewAgentViewModel(_service, _projects(), Ui);
        await Ui.ShowNewAgentAsync(dialog);
        await RefreshRowsAsync();
    }

    /// <summary>
    /// Issues a new token and puts it into the file of the agent that the user names. The old token
    /// ends at once, so the file is looked at first: a token is not issued for a file that cannot take it.
    /// The secret key of the agent is not kept here, so there is no other way to a complete file.
    /// </summary>
    [RelayCommand]
    private async Task NewTokenAsync(AgentRowViewModel row)
    {
        Notice = null;
        Error = null;
        if (!row.CanIssueToken)
            return;

        if (!await Ui.ConfirmAsync(
                $"New token for {row.Name}",
                "The token of this agent ends at once, and its old credentials file stops working. "
                + "Choose the credentials file of this agent: it gets the new token and keeps everything else. "
                + "Without that file the agent has to be made again.",
                "Choose file"))
        {
            return;
        }

        var path = await Ui.PickOpenFileAsync($"Credentials file of {row.Name}");
        if (path is null)
            return;

        IsBusy = true;
        try
        {
            AgentCredentialsFile.CheckBelongsTo(path, row.Info.Id);
            var (token, expiresAt) = await _service.NewTokenAsync(row.Info.Id);
            try
            {
                AgentCredentialsFile.ReplaceToken(path, row.Info.Id, token, expiresAt);
            }
            catch (IOException ex)
            {
                Error = $"A new token was issued, but the file could not be changed ({ex.Message}). "
                        + "Choose New token again to issue another one.";
                return;
            }

            Notice = $"New token for {row.Name}, valid until {expiresAt.ToLocalTime():yyyy-MM-dd}. The file was updated.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshRowsAsync();
    }

    [RelayCommand]
    private async Task RevokeAsync(AgentRowViewModel row)
    {
        Notice = null;
        Error = null;
        if (!row.CanRevoke)
            return;

        if (!await Ui.ConfirmAsync(
                $"Revoke {row.Name}",
                "The token ends and the agent loses its projects at once. The agent is not deleted, and its name is free again. "
                + "Project keys that it already knows stay known to it: if its file may have leaked, rotate the project keys too.",
                "Revoke"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _service.RevokeAsync(row.Info.Id);
            Notice = $"{row.Name} was revoked.";
        }
        catch (IOException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshRowsAsync();
    }
}
