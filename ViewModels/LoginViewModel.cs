using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

/// <summary>
/// Signs this device in to the server (SERVER.md, "Authentication"): the user name and the server
/// come from the settings, the password and the name of the device are typed here. The password is
/// the one of the account, not the passphrase of the keys.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly ServerApi _api;

    public LoginViewModel(ServerApi api, string serverUrl, string userName, string? deviceName = null)
    {
        _api = api;
        ServerUrl = serverUrl;
        UserName = userName;
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? Environment.MachineName : deviceName;
    }

    public string ServerUrl { get; }

    public string UserName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    private string _password = "";

    /// <summary>The name under which the server lists this device, so that it can be revoked by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    private string _deviceName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    public bool CanSignIn => !IsBusy && Password.Length > 0 && !string.IsNullOrWhiteSpace(DeviceName);

    /// <summary>Signs in. Returns true when the device is signed in; <see cref="Error"/> says what went wrong otherwise.</summary>
    public async Task<bool> SignInAsync()
    {
        if (!CanSignIn)
            return false;

        Error = null;
        IsBusy = true;
        try
        {
            await _api.LoginAsync(UserName, Password, DeviceName.Trim());
            return true;
        }
        catch (ServerApiException ex)
        {
            Error = Describe(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The password that was typed, handed over once and then forgotten here. The setup of the keys
    /// needs it to refuse the same text as a passphrase.
    /// </summary>
    public string TakePassword()
    {
        var password = Password;
        Password = "";
        return password;
    }

    private static string Describe(ServerApiException ex)
    {
        if (ex.Status == 401)
            return "The user name or the password is wrong.";

        if (ex.Status == 429)
        {
            return ex.RetryAfter is { } wait
                ? $"Too many failed attempts. Try again in {Minutes(wait)}."
                : "Too many failed attempts. Try again later.";
        }

        // Everything else: the server did not answer, or answered with something unexpected.
        return ex.Message;
    }

    private static string Minutes(TimeSpan wait)
    {
        var minutes = (int)Math.Ceiling(wait.TotalMinutes);
        return minutes <= 1 ? "a minute" : $"{minutes} minutes";
    }
}
