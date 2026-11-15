using OloopApp.Core.Io;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

/// <summary>The shell with the server as backend: sign-in at the load, one store, lock and sign out.</summary>
public class ServerShellTests : IDisposable
{
    private const string Passphrase = "Grüne Wiese 42!";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oloop-shell-" + Guid.NewGuid().ToString("N"));
    private readonly FakeOloopServer _server = new();
    private readonly MemorySecrets _secrets = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly Dictionary<Guid, MemoryRevisionLedger> _ledgers = [];

    public ServerShellTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MainViewModel NewShell()
    {
        var shell = new MainViewModel(new OloopSettings
        {
            FilePath = Path.Combine(_dir, "settings.json"),
            Backend = "server",
            ServerUrl = FakeOloopServer.BaseUrl,
            ServerUser = "timo",
            Actor = "claude",
        })
        {
            Dialogs = _dialogs,
            Session = new ServerSession(_server.CreateClient(), _secrets, () => _dialogs,
                (_, userId) => _ledgers.TryGetValue(userId, out var ledger) ? ledger : _ledgers[userId] = new MemoryRevisionLedger()),
        };
        return shell;
    }

    private async Task<MainViewModel> FirstStartAsync()
    {
        _dialogs.Password = "secret";
        _dialogs.SetupPassphrase = Passphrase;
        var shell = NewShell();
        await shell.RefreshAsync();
        return shell;
    }

    [Fact]
    public async Task The_First_Load_Signs_In_Sets_Up_The_Keys_And_Shows_An_Empty_Server()
    {
        using var shell = await FirstStartAsync();

        Assert.Equal(1, _dialogs.Logins);
        Assert.Equal(1, _dialogs.Setups);
        Assert.Equal("", shell.StatusMessage);
        Assert.Empty(shell.Projects);
        Assert.False(shell.Dashboard.HasNoDataRoot); // the card "choose a data folder" is for the folder only
        Assert.NotNull(shell.StoreOrNull);
    }

    [Fact]
    public async Task A_Project_Written_Through_The_Shell_Is_There_After_The_Next_Start_Without_A_Dialog()
    {
        using (var shell = await FirstStartAsync())
        {
            await shell.StoreOrNull!.CreateProjectAsync("Server project");
            await shell.RefreshAsync();
            Assert.Single(shell.Projects);
        }

        _dialogs.Reset();
        using var again = NewShell();
        await again.RefreshAsync();

        Assert.Equal(0, _dialogs.Logins + _dialogs.Setups + _dialogs.Unlocks);
        Assert.Equal("Server project", Assert.Single(again.Projects).Name);
    }

    [Fact]
    public async Task The_Store_Is_One_Instance_Across_Reloads()
    {
        using var shell = await FirstStartAsync();
        var first = shell.StoreOrNull;

        await shell.RefreshAsync();

        Assert.Same(first, shell.StoreOrNull);
        Assert.Equal(1, _dialogs.Logins); // no second dialog
    }

    [Fact]
    public async Task Cancelling_The_Login_Says_So_And_Shows_Nothing()
    {
        _dialogs.Cancel = true;
        using var shell = NewShell();

        await shell.RefreshAsync();

        Assert.Contains("Not signed in", shell.StatusMessage);
        Assert.Empty(shell.Projects);
        Assert.IsType<UnavailableServerStore>(shell.StoreOrNull);
    }

    [Fact]
    public async Task A_Server_That_Is_Down_Is_Named_As_Such_And_A_Reload_Recovers()
    {
        using var shell = await FirstStartAsync();
        shell.Session.Lock(); // a new session of the same shell: nothing open
        _server.Down = true;
        _dialogs.UnlockInput = Passphrase;

        await shell.RefreshAsync();
        Assert.Contains("reach the server", shell.StatusMessage);

        _server.Down = false;
        await shell.RefreshAsync();
        Assert.Equal("", shell.StatusMessage);
    }

    [Fact]
    public async Task A_Write_On_A_Session_That_Is_Not_Open_Fails_With_The_Reason_And_Writes_Nothing()
    {
        using var shell = await FirstStartAsync();
        var project = await shell.StoreOrNull!.CreateProjectAsync("P");
        shell.Session.Lock();

        var store = shell.StoreOrNull!;
        var error = await Assert.ThrowsAsync<IOException>(() => store.CreateProjectAsync("Q"));
        Assert.Contains("Not signed in", error.Message);
        Assert.Equal(1, _server.Stored.Count(d => d.Kind == "project"));
        Assert.NotNull(project);
    }

    // ---------------- The settings dialog ----------------

    [Fact]
    public async Task The_Settings_Offer_The_Server_And_Show_Where_The_Session_Stands()
    {
        using var shell = await FirstStartAsync();
        SettingsViewModel? seen = null;
        _dialogs.OnSettings = vm =>
        {
            seen = vm;
            return Task.FromResult(false);
        };

        await shell.OpenSettingsAsync();

        Assert.NotNull(seen);
        Assert.True(seen!.ServerBackendAvailable);
        Assert.True(seen.HasServerControl);
        Assert.Contains("kept on this computer", seen.ServerStatus);
        Assert.True(seen.CanLockKeys);
        Assert.True(seen.CanSignOut);
    }

    [Fact]
    public async Task Lock_Keys_Forgets_The_Key_Shows_The_Reason_And_The_Next_Load_Asks_For_The_Passphrase_Only()
    {
        using var shell = await FirstStartAsync();
        await shell.StoreOrNull!.CreateProjectAsync("P");
        await shell.RefreshAsync();
        Assert.Single(shell.Projects);

        _dialogs.OnSettings = async vm =>
        {
            await vm.LockKeysCommand.ExecuteAsync(null);
            Assert.Contains("locked", vm.ServerStatus);
            Assert.False(vm.CanLockKeys);
            Assert.True(vm.CanSignOut);
            return false;
        };
        await shell.OpenSettingsAsync();

        Assert.Contains("locked", shell.StatusMessage);
        Assert.Empty(shell.Projects);

        _dialogs.Reset();
        _dialogs.UnlockInput = Passphrase;
        await shell.RefreshAsync();

        Assert.Equal(0, _dialogs.Logins);
        Assert.Equal(1, _dialogs.Unlocks);
        Assert.Single(shell.Projects);
    }

    [Fact]
    public async Task Sign_Out_Forgets_Everything_And_The_Next_Load_Asks_For_The_Password_Again()
    {
        using var shell = await FirstStartAsync();

        _dialogs.OnSettings = async vm =>
        {
            await vm.SignOutCommand.ExecuteAsync(null);
            Assert.Contains("Not signed in", vm.ServerStatus);
            Assert.False(vm.CanSignOut);
            return false;
        };
        await shell.OpenSettingsAsync();

        Assert.Contains("Signed out", shell.StatusMessage);
        Assert.Empty(_secrets.Names);

        _dialogs.Reset();
        _dialogs.Password = "secret";
        _dialogs.UnlockInput = Passphrase;
        await shell.RefreshAsync();
        Assert.Equal(1, _dialogs.Logins);
    }

    [Fact]
    public async Task Switching_Back_To_The_Folder_Shows_The_Folder()
    {
        using var shell = await FirstStartAsync();
        var folder = Path.Combine(_dir, "data");
        Directory.CreateDirectory(folder);

        _dialogs.OnSettings = vm =>
        {
            vm.Backend = StorageBackend.Folder;
            vm.DataRoot = folder;
            vm.Save();
            return Task.FromResult(true);
        };
        await shell.OpenSettingsAsync();

        Assert.IsType<FileOloopStore>(shell.StoreOrNull);
        Assert.Equal("", shell.StatusMessage);
    }

    // ---------------- Seen revisions ----------------

    [Fact]
    public async Task The_Revisions_That_Were_Seen_Are_Kept_For_The_Next_Start()
    {
        using (var shell = await FirstStartAsync())
        {
            await shell.StoreOrNull!.CreateProjectAsync("Server project");
            await shell.RefreshAsync();
        }

        var ledger = Assert.Single(_ledgers.Values);
        Assert.Single(ledger.Load()); // the project
        Assert.All(ledger.Load().Values, rev => Assert.Equal(1, rev));
    }

    [Fact]
    public async Task Forget_Seen_Revisions_Reloads_The_Data()
    {
        using var shell = await FirstStartAsync();
        await shell.StoreOrNull!.CreateProjectAsync("Server project");
        await shell.RefreshAsync();
        var ledger = Assert.Single(_ledgers.Values);
        Assert.NotEmpty(ledger.Load());

        var reloaded = false;
        shell.DataReloaded += () => reloaded = true;
        _dialogs.OnSettings = async vm =>
        {
            Assert.True(vm.CanForgetRevisions);
            await vm.ForgetRevisionsCommand.ExecuteAsync(null);
            return false;
        };
        await shell.OpenSettingsAsync();

        // The reload that follows reads the server again; what it shows counts from here on.
        Assert.True(reloaded);
        Assert.Single(shell.Projects);
    }

    [Fact]
    public async Task Forget_Seen_Revisions_Works_While_The_Keys_Are_Locked()
    {
        using var shell = await FirstStartAsync();
        await shell.StoreOrNull!.CreateProjectAsync("Server project");
        await shell.RefreshAsync();
        var ledger = Assert.Single(_ledgers.Values);

        shell.Session.Lock();
        _dialogs.OnSettings = async vm =>
        {
            Assert.True(vm.CanForgetRevisions);
            await vm.ForgetRevisionsCommand.ExecuteAsync(null);
            return false;
        };
        await shell.OpenSettingsAsync();

        Assert.Empty(ledger.Load());
        Assert.Equal(0, _dialogs.Unlocks); // no unlock dialog was opened on top of the settings
    }

    [Fact]
    public async Task Without_A_Session_There_Is_Nothing_To_Forget()
    {
        using var shell = NewShell();
        SettingsViewModel? seen = null;
        _dialogs.OnSettings = vm =>
        {
            seen = vm;
            return Task.FromResult(false);
        };

        await shell.OpenSettingsAsync();

        Assert.False(seen!.CanForgetRevisions);
    }
}
