using System.Text;
using OloopApp.Core.Crypto;
using OloopApp.Core.Secrets;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

public class ServerSessionTests
{
    private const string Url = FakeOloopServer.BaseUrl;
    private const string Passphrase = "Grüne Wiese 42!";

    private readonly FakeOloopServer _server = new();
    private readonly MemorySecrets _secrets = new();
    private readonly ScriptedDialogs _dialogs = new();

    private ServerSession NewSession(ISecretStore? secrets = null)
        => new(_server.CreateClient(), secrets ?? _secrets, _dialogs);

    /// <summary>A first device: sign in and set up the keys. Returns the recovery code that was shown.</summary>
    private async Task<(ServerOloopStore Store, string Code)> FirstDeviceAsync(ServerSession session)
    {
        _dialogs.Password = "secret";
        _dialogs.SetupPassphrase = Passphrase;
        var store = await session.ConnectAsync(Url, "timo");
        return (store!, _dialogs.ShownRecoveryCode!);
    }

    // ---------------- First device ----------------

    [Fact]
    public async Task A_First_Device_Signs_In_Sets_Up_The_Keys_And_Keeps_Token_And_Key()
    {
        using var session = NewSession();
        var (store, code) = await FirstDeviceAsync(session);

        Assert.NotNull(store);
        Assert.Same(store, session.Store);
        Assert.Equal(13, code.Split('-').Length);
        Assert.Equal(1, _dialogs.Logins);
        Assert.Equal(1, _dialogs.Setups);
        Assert.Equal(0, _dialogs.Unlocks);

        Assert.NotNull(_server.Bundle);
        Assert.Contains("|refresh", string.Join(' ', _secrets.Names));
        Assert.Contains("|key", string.Join(' ', _secrets.Names));
        Assert.True(session.KeyIsKept);
    }

    [Fact]
    public async Task The_Password_Of_The_Login_Is_Refused_As_Passphrase()
    {
        using var session = NewSession();
        _dialogs.Password = "secret";
        _dialogs.SetupPassphrase = "secret";

        var store = await session.ConnectAsync(Url, "timo");

        Assert.Null(store); // the scripted dialog could not finish the setup, as a person would be stuck
        Assert.Null(_server.Bundle);
    }

    // ---------------- Next start ----------------

    [Fact]
    public async Task The_Next_Start_Asks_For_Nothing_When_Token_And_Key_Are_Kept()
    {
        using (var first = NewSession())
            await FirstDeviceAsync(first);
        _dialogs.Reset();

        using var second = NewSession();
        var store = await second.ConnectAsync(Url, "timo");

        Assert.NotNull(store);
        Assert.Equal(0, _dialogs.Logins + _dialogs.Setups + _dialogs.Unlocks);
        Assert.Empty((await store.LoadAsync()).Projects); // the store works: it reads with the resumed sign-in
    }

    [Fact]
    public async Task A_Rotated_Token_Is_Kept_So_That_The_Start_After_That_Works_Too()
    {
        using (var first = NewSession())
            await FirstDeviceAsync(first);

        for (var start = 0; start < 3; start++)
        {
            _dialogs.Reset();
            using var session = NewSession();
            Assert.NotNull(await session.ConnectAsync(Url, "timo"));
            Assert.Equal(0, _dialogs.Logins);
        }
    }

    [Fact]
    public async Task A_Revoked_Token_Leads_To_The_Login_And_The_Kept_Key_Still_Serves()
    {
        using (var first = NewSession())
            await FirstDeviceAsync(first);
        _server.RevokeRefreshTokens();
        _dialogs.Reset();
        _dialogs.Password = "secret";

        using var second = NewSession();
        Assert.NotNull(await second.ConnectAsync(Url, "timo"));

        Assert.Equal(1, _dialogs.Logins);
        Assert.Equal(0, _dialogs.Unlocks); // the key of this device is still good
    }

    [Fact]
    public async Task A_Server_That_Is_Down_Is_Not_Taken_For_A_Revoked_Token()
    {
        using (var first = NewSession())
            await FirstDeviceAsync(first);
        _dialogs.Reset();
        _server.Down = true;

        using var second = NewSession();
        await Assert.ThrowsAsync<ServerApiException>(() => second.ConnectAsync(Url, "timo"));

        Assert.Equal(0, _dialogs.Logins);
        Assert.Contains("|refresh", string.Join(' ', _secrets.Names)); // still there for when the server is back
    }

    // ---------------- Second device ----------------

    [Fact]
    public async Task A_Second_Device_Unlocks_With_The_Passphrase_And_Keeps_The_Key()
    {
        using (var first = NewSession())
            await FirstDeviceAsync(first);

        var otherSecrets = new MemorySecrets();
        _dialogs.Reset();
        _dialogs.Password = "secret";
        _dialogs.UnlockInput = Passphrase;

        using var second = NewSession(otherSecrets);
        Assert.NotNull(await second.ConnectAsync(Url, "timo"));

        Assert.Equal(1, _dialogs.Unlocks);
        Assert.Contains("|key", string.Join(' ', otherSecrets.Names));
    }

    [Fact]
    public async Task A_Kept_Key_That_Does_Not_Fit_The_Bundle_Is_Dropped_And_The_Passphrase_Asked()
    {
        using (var first = NewSession())
            await FirstDeviceAsync(first);
        var keyName = _secrets.Names.Single(n => n.EndsWith("|key"));
        _secrets.Set(keyName, new byte[32]); // some other key
        _dialogs.Reset();
        _dialogs.UnlockInput = Passphrase;

        using var second = NewSession();
        Assert.NotNull(await second.ConnectAsync(Url, "timo"));

        Assert.Equal(1, _dialogs.Unlocks);
        Assert.NotEqual(new byte[32], _secrets.Get(keyName));
    }

    // ---------------- Cancelling ----------------

    [Fact]
    public async Task Cancelling_Any_Dialog_Gives_No_Store_And_Keeps_Nothing_Of_The_Keys()
    {
        using var session = NewSession();
        _dialogs.Cancel = true;

        Assert.Null(await session.ConnectAsync(Url, "timo"));
        Assert.Null(session.Store);
        Assert.Empty(_secrets.Names);
    }

    // ---------------- Lock and sign out ----------------

    [Fact]
    public async Task Lock_Forgets_The_Key_But_Not_The_Sign_In()
    {
        using var session = NewSession();
        await FirstDeviceAsync(session);

        session.Lock();

        Assert.Null(session.Store);
        Assert.False(session.KeyIsKept);
        Assert.DoesNotContain("|key", string.Join(' ', _secrets.Names));
        Assert.Contains("|refresh", string.Join(' ', _secrets.Names));

        // Opening again asks for the passphrase and not for the password.
        _dialogs.Reset();
        _dialogs.UnlockInput = Passphrase;
        Assert.NotNull(await session.ConnectAsync(Url, "timo"));
        Assert.Equal(0, _dialogs.Logins);
        Assert.Equal(1, _dialogs.Unlocks);
    }

    [Fact]
    public async Task Sign_Out_Revokes_The_Device_And_Forgets_Everything()
    {
        using var session = NewSession();
        await FirstDeviceAsync(session);

        await session.SignOutAsync();

        Assert.Null(session.Store);
        Assert.Empty(_secrets.Names);

        // Nothing is kept: the next start has to sign in again.
        _dialogs.Reset();
        _dialogs.Password = "secret";
        _dialogs.UnlockInput = Passphrase;
        using var again = NewSession();
        Assert.NotNull(await again.ConnectAsync(Url, "timo"));
        Assert.Equal(1, _dialogs.Logins);
    }

    [Fact]
    public async Task Sign_Out_Forgets_What_Is_Kept_Even_When_The_Server_Is_Down()
    {
        using var session = NewSession();
        await FirstDeviceAsync(session);

        _server.Down = true;
        await session.SignOutAsync();

        Assert.Empty(_secrets.Names);
        Assert.Null(session.Store);
    }

    // ---------------- Scope ----------------

    [Fact]
    public async Task Connecting_Again_For_The_Same_Account_Gives_The_Same_Store()
    {
        using var session = NewSession();
        var (store, _) = await FirstDeviceAsync(session);

        Assert.Same(store, await session.ConnectAsync(Url, "TIMO"));
    }

    [Fact]
    public async Task A_Store_That_Keeps_Nothing_Asks_Again_At_The_Next_Start()
    {
        using (var first = NewSession(NoSecretStore.Instance))
            await FirstDeviceAsync(first);
        Assert.False(_dialogs.ShownRecoveryCode is null);

        _dialogs.Reset();
        _dialogs.Password = "secret";
        _dialogs.UnlockInput = Passphrase;
        using var second = NewSession(NoSecretStore.Instance);
        Assert.NotNull(await second.ConnectAsync(Url, "timo"));
        Assert.Equal(1, _dialogs.Logins);
        Assert.Equal(1, _dialogs.Unlocks);
        Assert.False(second.KeyIsKept);
    }

    // ---------------- Fakes ----------------

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _items = [];

        public bool IsPersistent => true;

        public IReadOnlyCollection<string> Names => _items.Keys;

        public byte[]? Get(string name) => _items.TryGetValue(name, out var v) ? (byte[])v.Clone() : null;

        public void Set(string name, byte[] value) => _items[name] = (byte[])value.Clone();

        public void Delete(string name) => _items.Remove(name);
    }

    /// <summary>Dialogs that do what a person would do, through the real view models.</summary>
    private sealed class ScriptedDialogs : IDialogService
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
        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel) => Task.FromResult(false);
        public Task<OloopApp.Core.Models.OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel) => Task.FromResult<OloopApp.Core.Models.OloopProject?>(null);
        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel) => Task.FromResult(false);
        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel) => Task.FromResult(ConflictChoice.Cancel);
        public Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles) => Task.FromResult(false);
    }
}
