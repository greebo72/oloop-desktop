namespace OloopApp.Core.Secrets;

/// <summary>Picks the store of the system the app runs on.</summary>
public static class SecretStoreFactory
{
    /// <summary>The service name under which the items are filed in the keychain.</summary>
    public const string ServiceName = "oloop";

    /// <summary>
    /// Another store in place of the one of the system. The tests set it, so that no test can read
    /// or change the tokens and keys of the user.
    /// </summary>
    public static Func<ISecretStore>? Override { get; set; }

    public static ISecretStore Create()
        => Override?.Invoke() ?? (OperatingSystem.IsMacOS() ? new MacKeychainSecretStore(ServiceName) : NoSecretStore.Instance);
}
