using OloopApp.Core.Secrets;
using Xunit;

namespace OloopApp.Tests;

/// <summary>A fact that runs on macOS only: the keychain is not there on other systems.</summary>
public sealed class MacOnlyFactAttribute : FactAttribute
{
    public MacOnlyFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "the macOS keychain is not on this system";
    }
}

public class SecretStoreTests
{
    [Fact]
    public void The_Store_Of_A_System_Without_One_Keeps_Nothing_And_Says_So()
    {
        var store = NoSecretStore.Instance;
        store.Set("a", [1, 2, 3]);
        Assert.False(store.IsPersistent);
        Assert.Null(store.Get("a"));
        store.Delete("a");
    }

    [Fact]
    public void The_Factory_Gives_A_Persistent_Store_Only_Where_There_Is_One()
        => Assert.Equal(OperatingSystem.IsMacOS(), SecretStoreFactory.Create().IsPersistent);

    // The real keychain, with a service name of its own so that the items of the app are not touched.
    private static MacKeychainSecretStore NewStore() => new("oloop-test-" + Guid.NewGuid().ToString("N"));

    [MacOnlyFact]
    public void A_Secret_Is_Stored_Read_Replaced_And_Deleted()
    {
        var store = NewStore();
        try
        {
            Assert.Null(store.Get("refresh"));

            store.Set("refresh", [1, 2, 3, 4]);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, store.Get("refresh"));

            store.Set("refresh", [9, 9]);
            Assert.Equal(new byte[] { 9, 9 }, store.Get("refresh"));

            store.Delete("refresh");
            Assert.Null(store.Get("refresh"));
            store.Delete("refresh"); // not there any more: no error
        }
        finally
        {
            store.Delete("refresh");
        }
    }

    [MacOnlyFact]
    public void Names_Do_Not_Mix_And_Binary_Data_Survives()
    {
        var store = NewStore();
        var key = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(); // every byte value, zeros included
        try
        {
            store.Set("secret key", key);
            store.Set("other", [7]);

            Assert.Equal(key, store.Get("secret key"));
            Assert.Equal(new byte[] { 7 }, store.Get("other"));
        }
        finally
        {
            store.Delete("secret key");
            store.Delete("other");
        }
    }

    [MacOnlyFact]
    public void Another_Service_Does_Not_See_The_Item()
    {
        var one = NewStore();
        var two = NewStore();
        try
        {
            one.Set("x", [1]);
            Assert.Null(two.Get("x"));
        }
        finally
        {
            one.Delete("x");
        }
    }

    [MacOnlyFact]
    public void Text_With_Non_Ascii_Letters_Is_A_Valid_Name()
    {
        var store = NewStore();
        try
        {
            store.Set("Schlüssel für Grüße 🔑", [5]);
            Assert.Equal(new byte[] { 5 }, store.Get("Schlüssel für Grüße 🔑"));
        }
        finally
        {
            store.Delete("Schlüssel für Grüße 🔑");
        }
    }
}
