namespace OloopApp.Core.Secrets;

/// <summary>Picks the store of the system the app runs on.</summary>
public static class SecretStoreFactory
{
    /// <summary>The service name under which the items are filed in the keychain.</summary>
    public const string ServiceName = "oloop";

    public static ISecretStore Create()
        => OperatingSystem.IsMacOS() ? new MacKeychainSecretStore(ServiceName) : NoSecretStore.Instance;
}
