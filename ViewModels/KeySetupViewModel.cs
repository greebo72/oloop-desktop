using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Crypto;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

/// <summary>The two screens of the key setup.</summary>
public enum KeySetupStep
{
    /// <summary>The user chooses the passphrase.</summary>
    Passphrase,

    /// <summary>The recovery code is shown, and the user confirms that it is kept.</summary>
    RecoveryCode,
}

/// <summary>
/// The first device of a user sets up the keys (SERVER.md, "Key hierarchy"): a passphrase, then
/// the recovery code. Nothing reaches the server before the user says that the code is kept.
/// </summary>
public partial class KeySetupViewModel : ViewModelBase
{
    /// <summary>The least length of a passphrase, in characters as a person counts them.</summary>
    public const int MinLength = 12;

    private readonly KeyService _keys;
    private readonly string? _loginPassword;
    private PendingKeySetup? _pending;

    /// <param name="keys">The key service of the signed-in user.</param>
    /// <param name="loginPassword">
    /// The password of the login, if this device has it: the spec asks for another passphrase, so
    /// the same text is refused.
    /// </param>
    public KeySetupViewModel(KeyService keys, string? loginPassword = null)
    {
        _keys = keys;
        _loginPassword = string.IsNullOrEmpty(loginPassword) ? null : loginPassword;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(PassphraseHint))]
    private string _passphrase = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(PassphraseHint))]
    private string _confirmation = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPassphraseStep), nameof(IsRecoveryStep))]
    private KeySetupStep _step = KeySetupStep.Passphrase;

    [ObservableProperty]
    private string _recoveryCodeText = "";

    /// <summary>The user has written the code down or saved it: the condition to finish.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFinish))]
    private bool _codeIsKept;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(CanFinish))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    /// <summary>The unlocked user once the setup is done; null until then.</summary>
    public UnlockedUser? Result { get; private set; }

    public bool IsPassphraseStep => Step == KeySetupStep.Passphrase;

    public bool IsRecoveryStep => Step == KeySetupStep.RecoveryCode;

    /// <summary>What is wrong with the input so far, or null. Empty input says nothing yet.</summary>
    public string? PassphraseHint
    {
        get
        {
            if (Passphrase.Length == 0)
                return null;
            if (Length(Passphrase) < MinLength)
                return $"At least {MinLength} characters.";
            if (_loginPassword is not null && Passphrase == _loginPassword)
                return "Choose another one than your password for the server.";
            if (Confirmation.Length > 0 && Confirmation != Passphrase)
                return "The two entries are not the same.";
            return null;
        }
    }

    public bool CanContinue => !IsBusy && Passphrase.Length > 0 && Confirmation == Passphrase && PassphraseHint is null;

    public bool CanFinish => !IsBusy && CodeIsKept && _pending is not null;

    /// <summary>Makes the keys (a second of work) and shows the recovery code. Returns false when it did not work.</summary>
    public async Task<bool> ContinueAsync()
    {
        if (!CanContinue)
            return false;

        Error = null;
        IsBusy = true;
        try
        {
            _pending = await _keys.PrepareSetupAsync(Passphrase);
            RecoveryCodeText = _pending.RecoveryCodeText;
            CodeIsKept = false;
            Step = KeySetupStep.RecoveryCode;
            return true;
        }
        catch (OloopCryptoException ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Stores the bundle. Returns true when the keys are set up and <see cref="Result"/> is the user.</summary>
    public async Task<bool> FinishAsync()
    {
        if (!CanFinish)
            return false;

        Error = null;
        IsBusy = true;
        try
        {
            Result = await _keys.CommitSetupAsync(_pending!);
            return true;
        }
        catch (ServerApiException ex) when (ex.IsConflict)
        {
            Error = "Another device has set up the keys in the meantime. Cancel this dialog and unlock with the passphrase of that device.";
            return false;
        }
        catch (IOException ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Back to the passphrase. The code that was made is dropped: a new one is made on the next step.</summary>
    public void GoBack()
    {
        _pending = null;
        RecoveryCodeText = "";
        CodeIsKept = false;
        Error = null;
        Step = KeySetupStep.Passphrase;
    }

    private static int Length(string text) => new StringInfo(text).LengthInTextElements;
}
