using System.Runtime.CompilerServices;
using OloopApp.Core.Io;
using OloopApp.Core.Secrets;

namespace OloopApp.Tests;

/// <summary>
/// Runs before the first test. A test must not read the settings of the user, write to them,
/// or use the tokens in the keychain: a view model that is made without settings loads the
/// default file, and with the server as backend in it a test would sign in to the real server
/// with the real token and use it up.
/// </summary>
internal static class TestIsolation
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        OloopSettings.PathOverride = Path.Combine(
            Path.GetTempPath(), "oloop-tests-settings-" + Guid.NewGuid().ToString("N"), "settings.json");

        SecretStoreFactory.Override = () => new MemorySecrets();
    }
}

public class TestIsolationTests
{
    [Xunit.Fact]
    public void A_Test_Reads_Neither_The_Settings_Nor_The_Keychain_Of_The_User()
    {
        var real = Path.Combine(OloopSettings.AppDataFolder(), "oloop", "settings.json");
        Xunit.Assert.NotEqual(real, OloopSettings.DefaultPath);
        Xunit.Assert.StartsWith(Path.GetTempPath(), OloopSettings.DefaultPath);

        // The default settings of a test are the empty ones: the folder, no server.
        Xunit.Assert.Equal(StorageBackend.Folder, OloopSettings.Load().ResolveBackend());

        Xunit.Assert.IsType<MemorySecrets>(SecretStoreFactory.Create());
    }
}
