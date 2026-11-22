using OloopApp.Core.Secrets;
using OloopApp.Core.Server;
using OloopApp.ViewModels;

namespace OloopApp.Tests;

internal sealed class MemorySecrets : ISecretStore
{
    private readonly Dictionary<string, byte[]> _items = [];

    public bool IsPersistent => true;

    public IReadOnlyCollection<string> Names => _items.Keys;

    public byte[]? Get(string name) => _items.TryGetValue(name, out var v) ? (byte[])v.Clone() : null;

    public void Set(string name, byte[] value) => _items[name] = (byte[])value.Clone();

    public void Delete(string name) => _items.Remove(name);
}

/// <summary>Dialogs that do what a person would do, through the real view models.</summary>
internal sealed class ScriptedDialogs : IDialogService
{
    public string Password = "";
    public string SetupPassphrase = "";
    public string UnlockInput = "";
    public bool Cancel;
    public string? ShownRecoveryCode;
    public int Logins, Setups, Unlocks;

    public void Reset()
    {
        Password = SetupPassphrase = UnlockInput = "";
        Cancel = false;
        ShownRecoveryCode = null;
        Logins = Setups = Unlocks = 0;
    }

    public async Task<bool> ShowLoginAsync(LoginViewModel vm)
    {
        Logins++;
        if (Cancel)
            return false;
        vm.Password = Password;
        return await vm.SignInAsync();
    }

    public async Task<UnlockedUser?> ShowKeySetupAsync(KeySetupViewModel vm)
    {
        Setups++;
        if (Cancel)
            return null;
        vm.Passphrase = SetupPassphrase;
        vm.Confirmation = SetupPassphrase;
        if (!await vm.ContinueAsync())
            return null;
        ShownRecoveryCode = vm.RecoveryCodeText;
        vm.CodeIsKept = true;
        return await vm.FinishAsync() ? vm.Result : null;
    }

    public async Task<UnlockedUser?> ShowKeyUnlockAsync(KeyUnlockViewModel vm)
    {
        Unlocks++;
        if (Cancel)
            return null;
        vm.Input = UnlockInput;
        return await vm.UnlockAsync() ? vm.Result : null;
    }

    // The dialogs of the rest of the app are not needed here.
    public Func<SettingsViewModel, Task<bool>>? OnSettings;

    public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel) => OnSettings?.Invoke(viewModel) ?? Task.FromResult(false);
    public Task<OloopApp.Core.Models.OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel) => Task.FromResult<OloopApp.Core.Models.OloopProject?>(null);
    public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel) => Task.FromResult(false);
    public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel) => Task.FromResult(ConflictChoice.Cancel);
    public Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles) => Task.FromResult(false);
}
