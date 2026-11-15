using CommunityToolkit.Mvvm.ComponentModel;
using OloopApp.Core.Crypto;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

/// <summary>
/// A device opens the key bundle of the user: with the passphrase, or, when that is lost, with
/// the recovery code (SERVER.md, "Key hierarchy").
/// </summary>
public partial class KeyUnlockViewModel : ViewModelBase
{
    private readonly KeyService _keys;
    private readonly KeyBundle _bundle;

    public KeyUnlockViewModel(KeyService keys, KeyBundle bundle)
    {
        _keys = keys;
        _bundle = bundle;
    }

    /// <summary>What the user typed: the passphrase, or the recovery code.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnlock))]
    private string _input = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesPassphrase))]
    private bool _usesRecoveryCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnlock))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    public UnlockedUser? Result { get; private set; }

    public bool UsesPassphrase => !UsesRecoveryCode;

    public bool CanUnlock => !IsBusy && Input.Length > 0;

    /// <summary>Switches between passphrase and recovery code; what was typed is dropped.</summary>
    public void SwitchKind()
    {
        UsesRecoveryCode = !UsesRecoveryCode;
        Input = "";
        Error = null;
    }

    /// <summary>Tries the input. Returns true when the bundle opened and <see cref="Result"/> is the user.</summary>
    public async Task<bool> UnlockAsync()
    {
        if (!CanUnlock)
            return false;

        Error = null;
        IsBusy = true;
        try
        {
            Result = UsesRecoveryCode
                ? _keys.UnlockWithRecoveryCode(_bundle, Input)
                : await _keys.UnlockWithPassphraseAsync(_bundle, Input);
            return true;
        }
        catch (UnknownEnvelopeException ex)
        {
            Error = ex.Message;
            return false;
        }
        catch (OloopCryptoException)
        {
            // The same words for a wrong key and a text that is no code: nothing for a guesser.
            Error = UsesRecoveryCode
                ? "That recovery code does not open your keys. Check it and try again."
                : "That passphrase does not open your keys. Check it and try again.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
