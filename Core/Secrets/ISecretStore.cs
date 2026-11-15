namespace OloopApp.Core.Secrets;

/// <summary>
/// Where this device keeps what must survive a restart but must not lie around in a file: the
/// refresh token of the server, and the secret key of the user once it has been unlocked. The
/// operating system guards it (the keychain on macOS).
/// </summary>
public interface ISecretStore
{
    /// <summary>True when what is set stays after the app is closed. False for a store that keeps nothing.</summary>
    bool IsPersistent { get; }

    /// <summary>The secret of that name, or null when there is none.</summary>
    /// <exception cref="IOException">The store could not be read (a locked keychain the user did not open).</exception>
    byte[]? Get(string name);

    /// <summary>Stores the secret, replacing an earlier one of the same name.</summary>
    /// <exception cref="IOException">The store could not be written.</exception>
    void Set(string name, byte[] value);

    /// <summary>Removes the secret. A name that is not there is no error.</summary>
    void Delete(string name);
}
