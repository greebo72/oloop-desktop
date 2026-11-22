using OloopApp.Core.Io;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The choice of the backend: the folder stays the default, an old settings
/// file keeps working, and the address of a server is checked.
/// </summary>
public class BackendSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "oloop-backend-" + Guid.NewGuid().ToString("N"));

    public BackendSettingsTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    // ---------------- The settings ----------------

    [Fact]
    public void The_Folder_Is_The_Default_Backend()
        => Assert.Equal(StorageBackend.Folder, new OloopSettings().ResolveBackend());

    [Fact]
    public void A_Settings_File_Without_A_Backend_Uses_The_Folder()
    {
        File.WriteAllText(SettingsPath, """{ "dataRoot": "/data", "actor": "alice" }""");

        var settings = OloopSettings.Load(SettingsPath);

        Assert.Equal(StorageBackend.Folder, settings.ResolveBackend());
        Assert.Equal("/data", settings.DataRoot);
    }

    [Fact]
    public void An_Unknown_Backend_Uses_The_Folder()
    {
        File.WriteAllText(SettingsPath, """{ "backend": "carrier-pigeon" }""");

        Assert.Equal(StorageBackend.Folder, OloopSettings.Load(SettingsPath).ResolveBackend());
    }

    [Fact]
    public void The_Server_Settings_Survive_A_Restart_And_Hold_No_Password()
    {
        new OloopSettings
        {
            Backend = "server",
            ServerUrl = "https://oloop.example.org",
            ServerUser = "alice",
        }.Save(SettingsPath);

        var loaded = OloopSettings.Load(SettingsPath);

        Assert.Equal(StorageBackend.Server, loaded.ResolveBackend());
        Assert.Equal("https://oloop.example.org", loaded.ServerUrl);
        Assert.Equal("alice", loaded.ServerUser);
        Assert.DoesNotContain("password", File.ReadAllText(SettingsPath), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------- The address ----------------

    [Theory]
    [InlineData("https://oloop.example.org", "https://oloop.example.org")]
    [InlineData("  https://oloop.example.org/  ", "https://oloop.example.org")]
    [InlineData("https://example.org/oloop/", "https://example.org/oloop")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("http://127.0.0.1:8000/", "http://127.0.0.1:8000")]
    public void A_Good_Address_Is_Normalized(string input, string expected)
    {
        Assert.Equal(expected, ServerEndpoint.Normalize(input, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("oloop.example.org")]
    [InlineData("ftp://oloop.example.org")]
    [InlineData("http://oloop.example.org")]
    [InlineData("https://alice:secret@oloop.example.org")]
    [InlineData("https://oloop.example.org/?a=b")]
    public void A_Bad_Address_Is_Refused_With_A_Reason(string input)
    {
        Assert.Null(ServerEndpoint.Normalize(input, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ---------------- The dialog ----------------

    [Fact]
    public void The_Dialog_Does_Not_Offer_A_Server_By_Default()
        => Assert.False(new SettingsViewModel(new OloopSettings()).ServerBackendAvailable);

    [Fact]
    public void A_Server_Needs_An_Address_And_A_User()
    {
        var vm = new SettingsViewModel(new OloopSettings { Actor = "alice", FilePath = SettingsPath })
        {
            ServerBackendAvailable = true,
        };

        vm.BackendIndex = 1;
        Assert.True(vm.IsServerBackend);
        Assert.False(vm.CanSave);

        vm.ServerUrl = "http://oloop.example.org";
        vm.ServerUser = "alice";
        Assert.NotNull(vm.Error);

        vm.ServerUrl = "https://oloop.example.org/";
        Assert.Null(vm.Error);
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void Saving_A_Server_Writes_The_Normalized_Address_And_Keeps_The_Folder()
    {
        var settings = new OloopSettings { Actor = "alice", DataRoot = _folder };
        var vm = new SettingsViewModel(settings) { ServerBackendAvailable = true };

        vm.BackendIndex = 1;
        vm.ServerUrl = "https://oloop.example.org/";
        vm.ServerUser = " alice ";
        vm.SaveTo(SettingsPath);

        Assert.Null(vm.Error);
        var loaded = OloopSettings.Load(SettingsPath);
        Assert.Equal(StorageBackend.Server, loaded.ResolveBackend());
        Assert.Equal("https://oloop.example.org", loaded.ServerUrl);
        Assert.Equal("alice", loaded.ServerUser);
        Assert.Equal(_folder, loaded.DataRoot);
    }

    [Fact]
    public void A_Folder_Needs_No_Server_Fields()
    {
        var vm = new SettingsViewModel(new OloopSettings { Actor = "alice", DataRoot = _folder });

        Assert.True(vm.IsFolderBackend);
        Assert.True(vm.CanSave);
        Assert.Null(vm.Error);
    }

    // ---------------- Availability ----------------

    [Fact]
    public void A_Missing_Folder_Is_Reported_By_The_Store()
    {
        var missing = Path.Combine(_folder, "gone");

        Assert.Null(new FileOloopStore(_folder).CheckAvailable());
        Assert.Equal($"The data folder '{missing}' does not exist.",
            new FileOloopStore(missing).CheckAvailable());
    }
}
